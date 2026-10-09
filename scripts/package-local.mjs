import {cpSync, existsSync, mkdirSync, mkdtempSync, renameSync} from 'node:fs';
import {basename, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const artifacts = join(root,'artifacts'); mkdirSync(artifacts,{recursive:true});
const staging = mkdtempSync(join(artifacts,'local-package-')), dest = join(staging,'VideoAtlas'); mkdirSync(dest);
const entries = ['src','tests','scripts','docs','deploy','vendor','.github','.agents','AGENTS.md','README.md','.env.example','.gitignore','.dockerignore','.editorconfig','Directory.Build.props','VideoAtlas.sln','Dockerfile','compose.yaml','global.json','nuget.config','dotnet-tools.json','package.json','package-lock.json','playwright.config.js','tasks.json'];
const excluded = new Set(['bin','obj','node_modules','.git','artifacts','data','TestResults','playwright-report']);
for (const entry of entries) if (existsSync(join(root,entry))) cpSync(join(root,entry),join(dest,entry),{recursive:true,filter:path=>!excluded.has(basename(path)) && (!basename(path).startsWith('.env') || basename(path)==='.env.example') && !/\.(?:db|log|pem|key)(?:-|$)/.test(path)});
for (const name of ['compose.local.yaml','LOCAL-TEST.md']) cpSync(join(root,'deploy/local',name),join(dest,name));
execFileSync('zip',['-q','-r',join(staging,'bundle.zip'),'VideoAtlas'],{cwd:staging,stdio:'inherit'});
const target = join(artifacts,'videoatlas-guncel-local-test.zip'); renameSync(join(staging,'bundle.zip'),target);
console.log(`Güncel paket: ${target}`);
console.log('Anahtar, kullanıcı verisi, derlenmiş dosya ve eski doğrulama paketleri dahil edilmedi.');
