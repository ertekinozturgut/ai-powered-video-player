import { defineConfig } from '@playwright/test';
import {mkdtempSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
const testDataRoot = process.env.E2E_URL ? undefined : mkdtempSync(join(tmpdir(), 'videoatlas-e2e-'));
export default defineConfig({
  testDir: './tests/browser', timeout: 90000, workers: 1,
  use: { baseURL: process.env.E2E_URL || 'http://127.0.0.1:5090', headless: true, viewport: {width: 1440, height: 1050},
    launchOptions: process.env.CHROMIUM_PATH ? {executablePath: process.env.CHROMIUM_PATH} : {}, screenshot: 'only-on-failure', trace: 'retain-on-failure' },
  outputDir: 'artifacts/browser', reporter: [['list']],
  webServer: process.env.E2E_URL ? undefined : {command: 'bash scripts/dev.sh', url: 'http://127.0.0.1:5090/health/ready', reuseExistingServer: false, timeout: 60000,
    env: {ASPNETCORE_URLS:'http://127.0.0.1:5090',Storage__Root:testDataRoot,Ai__ApiKey:'',ProviderCredentials__OpenRouter:'',ProviderCredentials__ElevenLabs:''}}
});
