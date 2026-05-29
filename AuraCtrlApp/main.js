const { app, BrowserWindow, ipcMain, Tray, Menu, nativeImage } = require('electron');
const { exec } = require('child_process');
const path = require('path');
const fs = require('fs');
const forge = require('node-forge');

let mainWindow = null;
let tray = null;
const SERVICE_PORT = 47201;
const SERVICE_NAME = 'AuraCtrlService';
const TOKEN_FILE = path.join(app.getPath('userData'), 'aura.token');
let trayPostesCount = 0;
let serviceStatus = 'unknown'; // running | stopped | unknown

function generateToken() {
  const token = forge.util.encode64(forge.random.getBytesSync(32));
  fs.writeFileSync(TOKEN_FILE, token, 'utf8');
  return token;
}

function getToken() {
  if (fs.existsSync(TOKEN_FILE)) return fs.readFileSync(TOKEN_FILE, 'utf8').trim();
  return generateToken();
}

function scExec(cmd, cb) {
  exec('sc ' + cmd + ' ' + SERVICE_NAME, (err, stdout) => {
    if (cb) cb(err, stdout);
  });
}

function getServiceStatus(cb) {
  exec('sc query ' + SERVICE_NAME, (err, stdout) => {
    if (err) { serviceStatus = 'stopped'; }
    else if (stdout.includes('RUNNING')) { serviceStatus = 'running'; }
    else if (stdout.includes('STOPPED')) { serviceStatus = 'stopped'; }
    else { serviceStatus = 'unknown'; }
    if (cb) cb(serviceStatus);
    updateTrayMenu();
    if (mainWindow) mainWindow.webContents.send('service-status', serviceStatus);
  });
}

function startService(cb) {
  scExec('start', () => { setTimeout(() => getServiceStatus(cb), 1500); });
}

function stopService(cb) {
  scExec('stop', () => { setTimeout(() => getServiceStatus(cb), 1500); });
}

function restartService(cb) {
  scExec('stop', () => { setTimeout(() => startService(cb), 2000); });
}

function getTrayIcon() {
  const iconPath = path.join(__dirname, 'assets', 'tray.png');
  if (fs.existsSync(iconPath)) return nativeImage.createFromPath(iconPath);
  return nativeImage.createEmpty();
}

function updateTrayMenu() {
  if (!tray) return;
  const running = serviceStatus === 'running';
  const statusLabel = running ? 'AuraCtrl — En service' : 'AuraCtrl — Service arrêté';
  const statusIcon  = running ? '🟢' : '🔴';
  tray.setToolTip(statusLabel);
  const menu = Menu.buildFromTemplate([
    { label: statusIcon + '  ' + (trayPostesCount > 0 ? trayPostesCount + ' poste(s) connecté(s)' : running ? 'En attente' : 'Service arrêté'), enabled: false },
    { type: 'separator' },
    { label: '🖥  Ouvrir l\'interface', click: () => showWindow() },
    { type: 'separator' },
    { label: '▶  Démarrer',  enabled: !running,  click: () => startService() },
    { label: '⏹  Arrêter',   enabled: running,   click: () => stopService() },
    { label: '↺  Redémarrer', enabled: running,  click: () => restartService() },
    { type: 'separator' },
    { label: '✖  Quitter', click: () => { app.quit(); } }
  ]);
  tray.setContextMenu(menu);
}

function createTray() {
  tray = new Tray(getTrayIcon());
  tray.on('double-click', () => showWindow());
  getServiceStatus();
  setInterval(() => getServiceStatus(), 10000);
}

function showWindow() {
  if (!mainWindow) { createWindow(); return; }
  if (mainWindow.isMinimized()) mainWindow.restore();
  mainWindow.show(); mainWindow.focus();
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1200, height: 760, minWidth: 900, minHeight: 600,
    frame: false, backgroundColor: '#080d1a',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true, nodeIntegration: false
    },
    icon: path.join(__dirname, 'assets', 'icon.png'),
    show: false
  });
  mainWindow.once('ready-to-show', () => mainWindow.show());
  const token = getToken();
  mainWindow.webContents.session.clearCache().then(() => {
    setTimeout(() => { mainWindow.loadURL('http://localhost:' + SERVICE_PORT + '?token=' + token); }, 1500);
  });
  mainWindow.on('close', (e) => { if (!app.isQuitting) { e.preventDefault(); mainWindow.hide(); } });
  mainWindow.on('closed', () => { mainWindow = null; });
}

ipcMain.on('window-minimize', () => mainWindow?.minimize());
ipcMain.on('window-maximize', () => { if (mainWindow?.isMaximized()) mainWindow.unmaximize(); else mainWindow?.maximize(); });
ipcMain.on('window-close',    () => mainWindow?.hide());
ipcMain.on('update-postes-count', (e, count) => { trayPostesCount = count; updateTrayMenu(); });
ipcMain.on('service-start',    () => startService());
ipcMain.on('service-stop',     () => stopService());
ipcMain.on('service-restart',  () => restartService());
ipcMain.on('service-status',   () => getServiceStatus());

function ensureServiceRunning(cb) {
  exec('sc query ' + SERVICE_NAME, (err, stdout) => {
    if (!stdout || !stdout.includes('RUNNING')) {
      console.log('Service non actif, démarrage...');
      exec('sc start ' + SERVICE_NAME, () => { setTimeout(cb, 2000); });
    } else {
      console.log('Service déjà actif');
      cb();
    }
  });
}

app.whenReady().then(() => {
  createTray();
  ensureServiceRunning(() => {
    serviceStatus = 'running';
    updateTrayMenu();
    createWindow();
  });
});
app.on('before-quit', () => { app.isQuitting = true; });
app.on('window-all-closed', () => {});
app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });


