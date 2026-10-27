package outputlayout

import (
	"os"
	"path/filepath"
	"testing"
)

func TestOnlyBChosenAndUserFolderClean(t *testing.T) {
	work, clean, err := WorkDirectory()
	if err != nil { t.Fatal(err) }
	defer clean()
	debug := filepath.Join(work, "DonOriginal3DMusic3_v1.0_RoomFirst_20261027")
	if err := os.MkdirAll(debug, 0755); err != nil { t.Fatal(err) }
	_ = os.WriteFile(filepath.Join(debug, "A_実測BRIR版.flac"), []byte("A"), 0644)
	_ = os.WriteFile(filepath.Join(debug, "v1.0_3D音響検査.txt"), []byte("debug"), 0644)
	b := filepath.Join(debug, "B_同じ部屋にH3直接音.flac")
	_ = os.WriteFile(b, make([]byte, 4096), 0644)
	selected, err := HybridB(work)
	if err != nil || selected != b { t.Fatalf("%s %v", selected, err) }
	outputRoot := t.TempDir()
	result, err := StoreHybridB(outputRoot, "Cats are dangerous.flac", "", work)
	if err != nil { t.Fatal(err) }
	if filepath.Base(result) != "Cats are dangerous_v1.0.flac" { t.Fatal(result) }
	files, err := os.ReadDir(filepath.Join(outputRoot, FolderName))
	if err != nil { t.Fatal(err) }
	if len(files) != 1 { t.Fatal("research files leaked into public output") }
}

func TestNoAccidentalASelectionAndRejectAmbiguousB(t *testing.T) {
	work := t.TempDir()
	_ = os.WriteFile(filepath.Join(work, "A_good.flac"), make([]byte, 4096), 0644)
	if _, err := HybridB(work); err == nil { t.Fatal("must not fall back to A") }
	_ = os.WriteFile(filepath.Join(work, "B_one.flac"), make([]byte, 4096), 0644)
	_ = os.WriteFile(filepath.Join(work, "B_two.flac"), make([]byte, 4096), 0644)
	if _, err := HybridB(work); err == nil { t.Fatal("must reject ambiguous B") }
}
