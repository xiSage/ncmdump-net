window.ncmApp = {
    dotNetRef: null,
    dragCounter: 0,

    init: function (dotNetRef) {
        this.dotNetRef = dotNetRef;

        document.addEventListener('dragenter', this._onDragEnter.bind(this));
        document.addEventListener('dragover', this._onDragOver);
        document.addEventListener('dragleave', this._onDragLeave.bind(this));
        document.addEventListener('drop', this._onDrop.bind(this));
    },

    dispose: function () {
        document.removeEventListener('dragenter', this._onDragEnter);
        document.removeEventListener('dragover', this._onDragOver);
        document.removeEventListener('dragleave', this._onDragLeave);
        document.removeEventListener('drop', this._onDrop);
        this.dotNetRef = null;
    },

    _onDragEnter: function (e) {
        e.preventDefault();
        this.dragCounter++;
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('SetDragOver', true);
        }
    },

    _onDragOver: function (e) {
        e.preventDefault();
    },

    _onDragLeave: function (e) {
        e.preventDefault();
        this.dragCounter--;
        if (this.dragCounter === 0 && this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('SetDragOver', false);
        }
    },

    _onDrop: async function (e) {
        e.preventDefault();
        this.dragCounter = 0;
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('SetDragOver', false);
        }

        const files = e.dataTransfer.files;
        for (let i = 0; i < files.length; i++) {
            const file = files[i];
            if (file.name.toLowerCase().endsWith('.ncm')) {
                const buffer = await file.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                if (this.dotNetRef) {
                    await this.dotNetRef.invokeMethodAsync('AddDroppedFile', file.name, bytes);
                }
            }
        }
    },

    // Opens the picker for the file input Blazor rendered. The click has to happen inside this
    // call so the browser still counts it as user-activated.
    clickElement: function (element) {
        element.click();
    },

    // Reads `count` bytes at `offset` from the file at `index` of the given file input and hands
    // them to .NET as a typed array (one bulk transfer per call). Blazor's own InputFile stream
    // moves the same bytes in 128 KiB interop round trips, which on Android is slow enough that a
    // multi-megabyte file looks like it was never picked at all.
    readFileChunk: async function (fileInput, index, offset, count) {
        const file = fileInput.files[index];
        if (!file) {
            return new Uint8Array(0);
        }

        const buffer = await file.slice(offset, offset + count).arrayBuffer();
        return new Uint8Array(buffer);
    },

    downloadFileFromStream: async function (fileName, streamRef) {
        const arrayBuffer = await streamRef.arrayBuffer();
        const blob = new Blob([arrayBuffer]);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    }
};
