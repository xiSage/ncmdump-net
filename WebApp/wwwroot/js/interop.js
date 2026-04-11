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
