package control

import (
	"context"
	"database/sql"
	"fmt"
	"net/url"
	"os"
	"os/exec"
	"os/user"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// Every integration invocation owns a new cluster. Tests never read a caller's
// database URL, PostgreSQL service configuration, or an existing data directory.
var privatePG struct{ root, bin, user string }

func pgCommand(name string, args ...string) ([]byte, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, filepath.Join(privatePG.bin, name), args...)
	for _, entry := range os.Environ() {
		if !strings.HasPrefix(entry, "PG") && !strings.HasPrefix(entry, "LC_ALL=") {
			cmd.Env = append(cmd.Env, entry)
		}
	}
	cmd.Env = append(cmd.Env, "LC_ALL=C", "PGSERVICEFILE=/dev/null", "PGPASSFILE=/dev/null")
	return cmd.CombinedOutput()
}
func postgresDSN(database string) string {
	u := url.URL{Scheme: "postgres", User: url.User(privatePG.user), Path: "/" + database}
	q := u.Query()
	q.Set("host", privatePG.root)
	q.Set("port", "15438")
	q.Set("sslmode", "disable")
	u.RawQuery = q.Encode()
	return u.String()
}
func startPostgres() error {
	output, err := pgCommand("pg_ctl", "-D", filepath.Join(privatePG.root, "data"), "-l", filepath.Join(privatePG.root, "postgres.log"), "-o", "-k "+privatePG.root+" -p 15438 -c listen_addresses='' -c fsync=on", "-w", "start")
	if err != nil {
		return fmt.Errorf("private postgres start: %w: %s", err, output)
	}
	return nil
}
func stopPostgres() error {
	output, err := pgCommand("pg_ctl", "-D", filepath.Join(privatePG.root, "data"), "-m", "fast", "-w", "stop")
	if err != nil {
		return fmt.Errorf("private postgres stop: %w: %s", err, output)
	}
	return nil
}
func TestMain(m *testing.M) {
	privatePG.bin = os.Getenv("ASHENWAKE_TEST_PG_BIN")
	if privatePG.bin == "" {
		path, err := exec.LookPath("pg_config")
		if err != nil {
			fmt.Fprintln(os.Stderr, "PostgreSQL binaries required for real transaction tests")
			os.Exit(1)
		}
		out, err := exec.Command(path, "--bindir").Output()
		if err != nil {
			os.Exit(1)
		}
		privatePG.bin = strings.TrimSpace(string(out))
	}
	current, err := user.Current()
	if err != nil {
		os.Exit(1)
	}
	privatePG.user = current.Username
	privatePG.root, err = os.MkdirTemp("/tmp", "awpg-")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	out, err := pgCommand("initdb", "-D", filepath.Join(privatePG.root, "data"), "-A", "trust", "--no-locale", "--encoding=UTF8")
	if err != nil {
		fmt.Fprintf(os.Stderr, "init private postgres: %v: %s\n", err, out)
		os.RemoveAll(privatePG.root)
		os.Exit(1)
	}
	if err = startPostgres(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.RemoveAll(privatePG.root)
		os.Exit(1)
	}
	code := m.Run()
	if err = stopPostgres(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		code = 1
	}
	os.RemoveAll(privatePG.root)
	os.Exit(code)
}
func testStore(t *testing.T) (*Store, string) {
	t.Helper()
	id, err := randomID()
	if err != nil {
		t.Fatal(err)
	}
	database := "test_" + id
	admin, err := sql.Open("pgx", postgresDSN("postgres"))
	if err != nil {
		t.Fatal(err)
	}
	_, err = admin.Exec(`CREATE DATABASE "` + database + `"`)
	admin.Close()
	if err != nil {
		t.Fatal(err)
	}
	store, err := Open(context.Background(), postgresDSN(database), "ws://127.0.0.1:8090/ws")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { store.Close() })
	return store, database
}
