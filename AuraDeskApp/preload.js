const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  minimize:           () => ipcRenderer.send('window-minimize'),
  maximize:           () => ipcRenderer.send('window-maximize'),
  close:              () => ipcRenderer.send('window-close'),
  updatePostesCount:  (n) => ipcRenderer.send('update-postes-count', n),
  serviceStart:       () => ipcRenderer.send('service-start'),
  serviceStop:        () => ipcRenderer.send('service-stop'),
  serviceRestart:     () => ipcRenderer.send('service-restart'),
  serviceStatus:      () => ipcRenderer.send('service-status'),
  onServiceStatus:    (cb) => ipcRenderer.on('service-status', (e, status) => cb(status))
});
