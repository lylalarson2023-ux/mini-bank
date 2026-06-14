window.setupFileUpload = function (dotnetRef, inputId) {
    var input = document.getElementById(inputId);
    if (!input) return;

    input.addEventListener('change', async function () {
        var file = input.files[0];
        if (!file) return;

        if (file.size > 2 * 1024 * 1024) {
            await dotnetRef.invokeMethodAsync('OnUploadError', 'Fichier trop volumineux (max 2 Mo)');
            input.value = '';
            return;
        }

        var fd = new FormData();
        fd.append('file', file);

        try {
            var res = await fetch('/api/upload', { method: 'POST', body: fd });
            var data = await res.json();
            if (data.url) {
                await dotnetRef.invokeMethodAsync('OnUploadComplete', data.url);
            } else {
                await dotnetRef.invokeMethodAsync('OnUploadError', data.error || 'Fichier refusé');
            }
        } catch (err) {
            await dotnetRef.invokeMethodAsync('OnUploadError', err.message);
        }

        input.value = '';
    });
};
