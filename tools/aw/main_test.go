package main

import (
	"os"
	"path/filepath"
	"testing"
)

func TestFindRootFromNestedDirectory(t *testing.T) {
	root := t.TempDir()
	if err := os.WriteFile(filepath.Join(root, "Ashenwake.sln"), nil, 0600); err != nil {
		t.Fatal(err)
	}
	nested := filepath.Join(root, "content", "skills")
	if err := os.MkdirAll(nested, 0700); err != nil {
		t.Fatal(err)
	}
	got, err := repositoryRoot(nested)
	if err != nil || got != root {
		t.Fatalf("got %q, %v; want %q", got, err, root)
	}
}

func TestMissingRootFails(t *testing.T) {
	if _, err := repositoryRoot(t.TempDir()); err == nil {
		t.Fatal("expected error outside a repository")
	}
}
