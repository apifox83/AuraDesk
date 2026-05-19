const { app, BrowserWindow, ipcMain } = require('electron');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const forge = require('node-forge');

let mainWindow = null;
let serviceProcess = null;
const SERVICE_PORT = 47201;
const TOKEN_FILE = path.join(app.getPath('userData'), 'aura.token');

function generateToken() {
  const token = forge.util.encode64(forge.random.getBytesSync(32));
  fs.writeFileSync(TOKEN_FILE, token, 'utf8');
  return token;
}

function getToken() {
  if (fs.existsSync(TOKEN_FILE)) return fs.readFileSync(TOKEN_FILE, 'utf8').trim();
  return generateToken();
}

function startService() {
  const token = generateToken();
  const exePath = path.join(path.dirname(app.getPath('exe')), 'service', 'AuraCtrlService.exe');
  const devPath = path.join(__dirname, '..', 'AuraCtrlService', 'bin', 'Debug', 'net10.0', 'win-x64', 'AuraCtrlService.exe');
  const svcPath = fs.existsSync(exePath) ? exePath : devPath;

  if (!fs.existsSync(svcPath)) {
    console.log('Service introuvable:', svcPath);
    return;
  }

  serviceProcess = spawn(svcPath, [], {
    env: { ...process.env, AURA_TOKEN: token, AURA_PORT: String(SERVICE_PORT) },
    detached: false,
    stdio: 'ignore'
  });

  serviceProcess.on('error', (err) => console.error('Service error:', err));
  serviceProcess.on('exit', (code) => console.log('Service exit:', code));
}

function stopService() {
  if (serviceProcess) {
    serviceProcess.kill();
    serviceProcess = null;
  }
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1200,
    height: 760,
    minWidth: 900,
    minHeight: 600,
    frame: false,
    backgroundColor: '#080d1a',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false
    },
    icon: path.join(__dirname, 'assets', 'icon.png')
  });

  const token = getToken();
  setTimeout(() => {
    mainWindow.loadURL(`http://localhost:${SERVICE_PORT}?token=${token}`);
  }, 2000);

  mainWindow.on('closed', () => { mainWindow = null; });
}

ipcMain.on('window-minimize', () => mainWindow?.minimize());
ipcMain.on('window-maximize', () => {
  if (mainWindow?.isMaximized()) mainWindow.unmaximize();
  else mainWindow?.maximize();
});
ipcMain.on('window-close', () => { stopService(); app.quit(); });

app.whenReady().then(() => {
  startService();
  createWindow();
});

app.on('window-all-closed', () => {
  stopService();
  if (process.platform !== 'darwin') app.quit();
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow();
});
