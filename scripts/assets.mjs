import { mkdir, copyFile } from 'node:fs/promises';
const root = new URL('../src/VideoAtlas.Web/wwwroot/lib/', import.meta.url);
await mkdir(root, {recursive: true});
await copyFile(new URL('../node_modules/bootstrap/dist/css/bootstrap.min.css', import.meta.url), new URL('bootstrap.min.css', root));
await copyFile(new URL('../node_modules/bootstrap/LICENSE', import.meta.url), new URL('bootstrap.LICENSE', root));
