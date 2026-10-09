import {readFileSync} from 'node:fs';
import {pathToFileURL} from 'node:url';

export function parseSrt(text) {
  const seconds = t => { const [h,m,s,ms] = t.split(/[:,.]/).map(Number); return h * 3600 + m * 60 + s + ms / 1000; };
  const segments = text.replace(/^\uFEFF/, '').replace(/\r/g, '').trim().split(/\n\s*\n/).map(block => {
    const match = block.match(/(?:^|\n)(\d{2,}:\d{2}:\d{2}[,.]\d{3}) --> (\d{2,}:\d{2}:\d{2}[,.]\d{3})\n([\s\S]+)/);
    if (!match) throw new Error('Geçersiz SRT bölümü.');
    const start = seconds(match[1]), end = seconds(match[2]);
    if (end <= start) throw new Error('Geçersiz zaman aralığı.');
    return {start,end,text:match[3].replace(/<[^>]*>/g,' ').trim()};
  });
  if (!segments.length) throw new Error('Boş SRT.');
  return segments;
}
export function words(text) { return text.normalize('NFC').toLocaleLowerCase('tr').match(/[\p{L}\p{N}]+/gu) ?? []; }
export function evaluate(reference, actual, pairedBoundaries = false) {
  const a = words(reference.map(s => s.text).join(' ')), b = words(actual.map(s => s.text).join(' '));
  if (!a.length) throw new Error('Referans metin boş.');
  if (a.length * b.length > 25_000_000) throw new Error('Karşılaştırmayı aynı zaman aralıklarına bölün (en fazla 25 milyon kelime çifti).');
  let row = Uint32Array.from({length:b.length + 1}, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    const next = new Uint32Array(b.length + 1); next[0] = i;
    for (let j = 1; j <= b.length; j++) next[j] = Math.min(row[j] + 1, next[j-1] + 1, row[j-1] + (a[i-1] === b[j-1] ? 0 : 1));
    row = next;
  }
  if (pairedBoundaries && reference.length !== actual.length) throw new Error('Eşleştirilmiş zaman ölçümü aynı sayıda, aynı konuşmalara ait bölümler gerektirir.');
  const deltas = pairedBoundaries ? reference.flatMap((s,i) => [Math.abs(s.start-actual[i].start), Math.abs(s.end-actual[i].end)]) : [];
  return {referenceWords:a.length,actualWords:b.length,wordEdits:row[b.length],wer:row[b.length]/a.length,
    normalization:'Turkish lowercase, Unicode letters/numbers; punctuation ignored',
    referenceSegments:reference.length,actualSegments:actual.length,
    boundaryMeanAbsoluteSeconds:deltas.length ? deltas.reduce((x,y)=>x+y,0)/deltas.length : null,
    boundaryMaxAbsoluteSeconds:deltas.length ? Math.max(...deltas) : null,
    note:'WER doğruluk yüzdesi değildir ve 1 değerini aşabilir. Zaman ölçümü yalnızca elle eşleştirilmiş bölümler için geçerlidir; anlam ve kaynak doğruluğu insan değerlendirmesi gerektirir.'};
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const [reference,actual,option] = process.argv.slice(2);
    if (!reference || !actual || (option && option !== '--paired-boundaries')) throw new Error('Kullanım: node scripts/evaluate-transcript.mjs reference.srt actual.srt [--paired-boundaries]');
    console.log(JSON.stringify(evaluate(parseSrt(readFileSync(reference,'utf8')),parseSrt(readFileSync(actual,'utf8')),option === '--paired-boundaries'), null, 2));
  } catch (e) { console.error(e.message); process.exitCode = 1; }
}
