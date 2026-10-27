package outputlayout

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestTitleAndFilename(t *testing.T) {
	tests := []struct {
		source, title, want string
	}{
		{`C:\Music\001-Test.flac`, "Cats are dangerous", "Cats are dangerous_v1.0.flac"},
		{"/a/FAKE LAND.flac", "", "FAKE LAND_v1.0.flac"},
		{"/a/track.flac", "あ/い:う?え*お", "あ_い_う_え_お_v1.0.flac"},
		{"/a/.flac", "  ", "Music_v1.0.flac"},
	}
	for _, tc := range tests {
		if got := FileName(tc.source, tc.title, 1); got != tc.want {
			t.Errorf("%q: got %q, want %q", tc.source, got, tc.want)
		}
	}
	if got := FileName("a.flac", "猫", 2); got != "猫(2)_v1.0.flac" {
		t.Fatal(got)
	}
}

func TestProbeTitle(t *testing.T) {
	j := []byte(`{"format":{"tags":{"TITLE":"Cats are dangerous"}}}`)
	if ProbeTitle(j) != "Cats are dangerous" { t.Fatal("metadata title not read") }
	if ProbeTitle([]byte("{broken")) != "" { t.Fatal("invalid JSON should fall back") }
}

func TestOutputNoDebugAndCollision(t *testing.T) {
	base := t.TempDir()
	work := t.TempDir()
	raw := filepath.Join(work, "generated.flac")
	if err := os.WriteFile(raw, []byte(strings.Repeat("F", 4096)), 0644); err != nil { t.Fatal(err) }
	one, err := StoreFLAC(base, "track.flac", "Cats are dangerous", raw)
	if err != nil { t.Fatal(err) }
	two, err := StoreFLAC(base, "track.flac", "Cats are dangerous", raw)
	if err != nil { t.Fatal(err) }
	if filepath.Base(one) != "Cats are dangerous_v1.0.flac" { t.Fatal(one) }
	if filepath.Base(two) != "Cats are dangerous(2)_v1.0.flac" { t.Fatal(two) }
	names, err := os.ReadDir(filepath.Join(base, "3D変換先"))
	if err != nil { t.Fatal(err) }
	if len(names) != 2 { t.Fatalf("expected only two FLAC, got %d", len(names)) }
	for _, f := range names {
		if !strings.HasSuffix(f.Name(), "_v1.0.flac") { t.Fatal(f.Name()) }
	}
}

func TestRejectInvalidInput(t *testing.T) {
	_, err := OutputDirectory("")
	if err == nil { t.Fatal("empty output must fail") }
	_, err = StoreFLAC(t.TempDir(), "a.flac", "", "debug.txt")
	if err == nil { t.Fatal("debug txt cannot be installed") }
}
