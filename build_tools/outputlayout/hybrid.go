package outputlayout

import (
	"errors"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

// WorkDirectory returns a private throwaway render location. The v1.62
// research renderer may create timestamp directories and debug TXT here,
// but NONE of those are installed in the user's output folder.
func WorkDirectory() (string, func(), error) {
	path, err := os.MkdirTemp("", "donoriginal3dmusic3-v1.0-render-*")
	if err != nil {
		return "", nil, err
	}
	return path, func() { _ = os.RemoveAll(path) }, nil
}

// HybridB finds ONLY the user-approved v1.62 "B_同じ部屋にH3直接音"
// render in a temporary research output tree. A and diagnostic TXT are
// never selected. Exactly one B FLAC is required; ambiguity is fatal.
func HybridB(workTree string) (string, error) {
	var matches []string
	err := filepath.WalkDir(workTree, func(path string, d fs.DirEntry, err error) error {
		if err != nil { return err }
		if d.IsDir() { return nil }
		name := d.Name()
		if strings.HasPrefix(name, "B_") && strings.EqualFold(filepath.Ext(name), ".flac") {
			matches = append(matches, path)
		}
		return nil
	})
	if err != nil {
		return "", err
	}
	if len(matches) != 1 {
		return "", errors.New("B版の3D FLACが1本だけ生成されたことを確認できません。A版や診断TXTは配布出力しません")
	}
	return matches[0], nil
}

// SourceTitle reads the ORIGINAL music file's Title tag, never the generated
// experimental B_ filename. Metadata failures correctly fall back to the
// original input basename in StoreHybridB.
func SourceTitle(ffprobe, source string) string {
	if strings.TrimSpace(ffprobe) == "" {
		return ""
	}
	cmd := exec.Command(ffprobe, "-v", "error", "-show_entries",
		"format_tags=title:stream_tags=title", "-of", "json", source)
	data, err := cmd.Output()
	if err != nil {
		return ""
	}
	return ProbeTitle(data)
}

// StoreHybridB selects the approved B path and exposes ONLY one FLAC
// directly under 3D変換先. Caller must defer the WorkDirectory cleanup
// even when rendering fails or the batch is cancelled.
func StoreHybridB(outputBase, originalSource, ffprobe, renderWorkTree string) (string, error) {
	candidate, err := HybridB(renderWorkTree)
	if err != nil {
		return "", err
	}
	return StoreFLAC(outputBase, originalSource,
		SourceTitle(ffprobe, originalSource), candidate)
}
