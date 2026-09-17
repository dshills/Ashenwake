package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
)

func repositoryRoot(start string) (string, error) {
	dir, err := filepath.Abs(start)
	if err != nil {
		return "", err
	}
	for {
		if _, err := os.Stat(filepath.Join(dir, "Ashenwake.sln")); err == nil {
			return dir, nil
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			return "", fmt.Errorf("run aw from within the Ashenwake repository")
		}
		dir = parent
	}
}

func main() {
	cwd, err := os.Getwd()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	root, err := repositoryRoot(cwd)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	// Domain validation and simulation live exclusively in the .NET executable.
	args := []string{"run", "--no-build", "--project", filepath.Join(root, "game", "Ashenwake.Tooling"), "--"}
	args = append(args, os.Args[1:]...)
	cmd := exec.Command("dotnet", args...)
	cmd.Dir = root
	cmd.Stdin, cmd.Stdout, cmd.Stderr = os.Stdin, os.Stdout, os.Stderr
	if err := cmd.Run(); err != nil {
		if failure, ok := err.(*exec.ExitError); ok {
			os.Exit(failure.ExitCode())
		}
		fmt.Fprintln(os.Stderr, "aw: source tools/env.sh and build the .NET tooling first:", err)
		os.Exit(1)
	}
}
