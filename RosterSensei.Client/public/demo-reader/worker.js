// Reads one demo off the page's thread. The page ends this worker when it's done, which frees
// all the memory the reader used.
importScripts('wasm_exec.js');

let ready;

function load(version) {
    if (!ready) {
        const go = new Go();
        const url = `demo-reader.wasm?v=${encodeURIComponent(version)}`;
        ready = WebAssembly.instantiateStreaming(fetch(url), go.importObject)
            // Some hosts serve .wasm without its content type, which instantiateStreaming refuses.
            .catch(() => fetch(url).then(response => response.arrayBuffer()).then(bytes => WebAssembly.instantiate(bytes, go.importObject)))
            .then(result => { go.run(result.instance); });
    }
    return ready;
}

self.onmessage = async ({ data }) => {
    let reported = 0;
    try {
        await load(data.version);
        const json = await self.readDemo(data.file, progress => {
            // At most a hundred updates, however many pieces the file is read in.
            if (progress - reported >= 0.01 || progress === 1) {
                reported = progress;
                self.postMessage({ type: 'progress', value: progress });
            }
        });
        self.postMessage({ type: 'done', summary: JSON.parse(json) });
    } catch (error) {
        self.postMessage({ type: 'failed', message: String(error?.message ?? error), patchVersion: error?.patchVersion || null });
    }
};
