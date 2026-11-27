package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"runtime"
	"time"

	"demoreader/reader"
)

func main() {
	f, err := os.Open(os.Args[1])
	if err != nil {
		panic(err)
	}
	defer f.Close()
	start := time.Now()
	s, _, err := reader.Read(bufio.NewReaderSize(f, 1<<20))
	if err != nil {
		panic(err)
	}
	b, _ := json.MarshalIndent(s, "", " ")
	fmt.Println(string(b))
	var m runtime.MemStats
	runtime.ReadMemStats(&m)
	fmt.Fprintf(os.Stderr, "%.1fs sys=%dMB\n", time.Since(start).Seconds(), m.Sys/1048576)
}
