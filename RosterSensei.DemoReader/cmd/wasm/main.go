//go:build js && wasm

// Runs in a Web Worker. Exposes readDemo(file, onProgress), which reads a demo File (plain, .gz, .bz2
// or .zip) piece by piece and resolves with the scoreboard as JSON, so a big demo is never held in
// memory at once.
package main

import (
	"archive/zip"
	"bufio"
	"compress/bzip2"
	"compress/gzip"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"runtime/debug"
	"strings"
	"syscall/js"

	"demoreader/reader"
)

// A demo can't really be bigger than this once unpacked; anything larger is a broken or hostile archive.
const maxUnpackedBytes = 3 << 30

func await(promise js.Value) (js.Value, error) {
	done := make(chan struct{})
	var value js.Value
	var failure error
	onOk := js.FuncOf(func(_ js.Value, args []js.Value) any {
		value = args[0]
		close(done)
		return nil
	})
	onErr := js.FuncOf(func(_ js.Value, args []js.Value) any {
		failure = errors.New(args[0].Call("toString").String())
		close(done)
		return nil
	})
	defer onOk.Release()
	defer onErr.Release()
	promise.Call("then", onOk, onErr)
	<-done
	return value, failure
}

// Reads a JS Blob through blob.slice(...).arrayBuffer(), one chunk at a time.
type blobReader struct {
	blob       js.Value
	size       int64
	offset     int64
	onProgress js.Value
}

func (b *blobReader) ReadAt(p []byte, off int64) (int, error) {
	if off >= b.size {
		return 0, io.EOF
	}
	end := min(off+int64(len(p)), b.size)
	buffer, err := await(b.blob.Call("slice", off, end).Call("arrayBuffer"))
	if err != nil {
		return 0, err
	}
	n := js.CopyBytesToGo(p, js.Global().Get("Uint8Array").New(buffer))
	if int64(n) < int64(len(p)) {
		return n, io.EOF
	}
	return n, nil
}

func (b *blobReader) Read(p []byte) (int, error) {
	n, err := b.ReadAt(p, b.offset)
	b.offset += int64(n)
	if b.onProgress.Type() == js.TypeFunction && b.size > 0 {
		b.onProgress.Invoke(float64(b.offset) / float64(b.size))
	}
	if err == io.EOF && n > 0 {
		err = nil
	}
	return n, err
}

func unpack(file *blobReader) (io.Reader, error) {
	magic := make([]byte, 8)
	n, _ := file.ReadAt(magic, 0)
	magic = magic[:n]

	var demo io.Reader
	switch {
	case strings.HasPrefix(string(magic), "PBDEMS2"):
		demo = bufio.NewReaderSize(file, 1<<20)
	case len(magic) >= 2 && magic[0] == 0x1f && magic[1] == 0x8b:
		gz, err := gzip.NewReader(bufio.NewReaderSize(file, 1<<20))
		if err != nil {
			return nil, err
		}
		demo = gz
	case strings.HasPrefix(string(magic), "BZh"):
		demo = bzip2.NewReader(bufio.NewReaderSize(file, 1<<20))
	case strings.HasPrefix(string(magic), "PK\x03\x04"):
		archive, err := zip.NewReader(file, file.size)
		if err != nil {
			return nil, err
		}
		var entry *zip.File
		for _, f := range archive.File {
			if strings.HasSuffix(strings.ToLower(f.Name), ".dem") {
				if entry != nil {
					return nil, errors.New("the zip holds more than one demo; zip one map at a time")
				}
				entry = f
			}
		}
		if entry == nil {
			return nil, errors.New("the zip holds no .dem file")
		}
		rc, err := entry.Open()
		if err != nil {
			return nil, err
		}
		// Progress follows the compressed bytes, which zip reads through ReadAt.
		demo = &progressReader{r: rc, total: float64(entry.UncompressedSize64), onProgress: file.onProgress}
	case strings.HasPrefix(string(magic), "HL2DEMO"):
		return nil, errors.New("this is a CS:GO demo; only CS2 demos can be imported")
	default:
		return nil, errors.New("this isn't a CS2 demo (.dem), or a .gz, .bz2 or .zip holding one")
	}
	return &capped{r: demo, left: maxUnpackedBytes}, nil
}

type progressReader struct {
	r          io.Reader
	read       float64
	total      float64
	onProgress js.Value
}

func (p *progressReader) Read(b []byte) (int, error) {
	n, err := p.r.Read(b)
	p.read += float64(n)
	if p.onProgress.Type() == js.TypeFunction && p.total > 0 {
		p.onProgress.Invoke(min(p.read/p.total, 1))
	}
	return n, err
}

type capped struct {
	r    io.Reader
	left int64
}

func (c *capped) Read(p []byte) (int, error) {
	if c.left <= 0 {
		return 0, errors.New("the unpacked demo is too big")
	}
	if int64(len(p)) > c.left {
		p = p[:c.left]
	}
	n, err := c.r.Read(p)
	c.left -= int64(n)
	return n, err
}

func readDemo(_ js.Value, args []js.Value) any {
	file := args[0]
	var onProgress js.Value
	if len(args) > 1 {
		onProgress = args[1]
	}
	return js.Global().Get("Promise").New(js.FuncOf(func(_ js.Value, settle []js.Value) any {
		resolve, reject := settle[0], settle[1]
		go func() {
			patch := 0
			fail := func(message string) {
				failure := js.Global().Get("Error").New(message)
				// The demo's CS2 patch, when its header was read, so the page can report a likely game update.
				failure.Set("patchVersion", patch)
				reject.Invoke(failure)
			}
			defer func() {
				if r := recover(); r != nil {
					fail(fmt.Sprint("couldn't read the demo: ", r))
				}
			}()
			source := &blobReader{blob: file, size: int64(file.Get("size").Float()), onProgress: onProgress}
			demo, err := unpack(source)
			if err != nil {
				fail(err.Error())
				return
			}
			summary, readPatch, err := reader.Read(demo)
			patch = readPatch
			if err != nil {
				fail(err.Error())
				return
			}
			out, _ := json.Marshal(summary)
			resolve.Invoke(string(out))
		}()
		return nil
	}))
}

func main() {
	// Fewer garbage collections: the browser has the memory, and single-threaded GC is the main cost.
	debug.SetGCPercent(400)
	js.Global().Set("readDemo", js.FuncOf(readDemo))
	select {}
}
