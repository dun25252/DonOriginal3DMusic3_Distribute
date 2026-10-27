// Package outputlayout enforces DonOriginal3DMusic3 Ver.1.0 distribution naming.
// It must be wired into the final render path; by itself it does NOT patch old EXEs.
package outputlayout

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"unicode"
	"unicode/utf8"
)

const (
	Version = "v1.0"
	FolderName = "3D変換先"
)

func OutputDirectory(base string) (string, error) {
	base = strings.TrimSpace(base)
	if base == "" {
		return "", errors.New("保存先を指定してください")
	}
	// This is the ONLY user-facing output folder. No date, stage or song subfolders.
	dst := filepath.Join(base, FolderName)
	if err := os.MkdirAll(dst, 0755); err != nil {
		return "", err
	}
	return dst, nil
}

// ProbeTitle understands the standard ffprobe -show_format -show_streams -of json
// metadata. Invalid/missing metadata is not an error: fall back to source basename.
func ProbeTitle(ffprobeJSON []byte) string {
	var p struct {
		Format struct { Tags map[string]string `json:"tags"` } `json:"format"`
		Streams []struct { Tags map[string]string `json:"tags"` } `json:"streams"`
	}
	if json.Unmarshal(ffprobeJSON, &p) != nil {
		return ""
	}
	read := func(m map[string]string) string {
		for k, v := range m {
			if strings.EqualFold(k, "title") && strings.TrimSpace(v) != "" {
				return strings.TrimSpace(v)
			}
		}
		return ""
	}
	if s := read(p.Format.Tags); s != "" {
		return s
	}
	for _, stream := range p.Streams {
		if s := read(stream.Tags); s != "" {
			return s
		}
	}
	return ""
}

// CleanTitle keeps Japanese, spaces and common punctuation while removing
// characters illegal in Windows file names and unsafe control characters.
// The research-mode prefixes are not prepended by this library.
func CleanTitle(title string) string {
	title = strings.TrimSpace(title)
	var b strings.Builder
	for _, r := range title {
		if r < 32 || r == 127 || unicode.IsControl(r) || strings.ContainsRune(`<>:"/\|?*`, r) {
			b.WriteRune('_')
		} else {
			b.WriteRune(r)
		}
	}
	clean := strings.Trim(b.String(), " .")
	for strings.Contains(clean, "__") {
		clean = strings.ReplaceAll(clean, "__", "_")
	}
	// Limit by UTF-8 bytes as well as runes, avoiding long Windows paths.
	var short strings.Builder
	for _, r := range clean {
		if utf8.RuneCountInString(short.String()) >= 96 || short.Len()+utf8.RuneLen(r) > 180 {
			break
		}
		short.WriteRune(r)
	}
	clean = strings.Trim(short.String(), " .")
	if clean == "" {
		return "Music"
	}
	return clean
}

func Title(sourcePath, taggedTitle string) string {
	if strings.TrimSpace(taggedTitle) != "" {
		return CleanTitle(taggedTitle)
	}
	base := filepath.Base(strings.ReplaceAll(sourcePath, "\\", "/"))
	return CleanTitle(strings.TrimSuffix(base, filepath.Ext(base)))
}

func FileName(sourcePath, taggedTitle string, number int) string {
	t := Title(sourcePath, taggedTitle)
	if number > 1 {
		t = fmt.Sprintf("%s(%d)", t, number)
	}
	return t + "_" + Version + ".flac"
}

// StoreFLAC installs exactly ONE complete FLAC in 3D変換先.
// The render pipeline MUST first write to a temporary working directory,
// not directly to this folder. Never copy A/B, *.txt, *.csv or *.log here.
// Exclusive file creation avoids overwriting repeated song titles.
func StoreFLAC(base, sourcePath, taggedTitle, renderedFLAC string) (string, error) {
	if !strings.EqualFold(filepath.Ext(renderedFLAC), ".flac") {
		return "", errors.New("入力レンダリング結果はFLACである必要があります")
	}
	dir, err := OutputDirectory(base)
	if err != nil {
		return "", err
	}
	src, err := os.Open(renderedFLAC)
	if err != nil {
		return "", err
	}
	defer src.Close()
	fi, err := src.Stat()
	if err != nil || fi.Size() < 1024 {
		return "", errors.New("変換済みFLACが存在しないか小さすぎます")
	}
	for n := 1; n < 10001; n++ {
		path := filepath.Join(dir, FileName(sourcePath, taggedTitle, n))
		dst, createErr := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0644)
		if os.IsExist(createErr) {
			continue
		}
		if createErr != nil {
			return "", createErr
		}
		ok := false
		func() {
			defer dst.Close()
			if _, e := io.Copy(dst, src); e == nil {
				ok = dst.Sync() == nil
			}
		}()
		if !ok {
			_ = os.Remove(path)
			return "", errors.New("FLACの保存に失敗しました。途中ファイルを削除しました")
		}
		return path, nil
	}
	return "", errors.New("同名ファイルが多すぎます")
}
