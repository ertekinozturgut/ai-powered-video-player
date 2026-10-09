import {test} from 'node:test';
import assert from 'node:assert/strict';
import {parseSrt,evaluate} from '../scripts/evaluate-transcript.mjs';
test('WER measures insertion deletion substitution and ignores Turkish case/punctuation', () => {
  const s = text => [{start:0,end:2,text}];
  assert.equal(evaluate(s('ÜRÜN bütçe İZİN'),s('ürün bütçe izin!')).wer,0);
  assert.equal(evaluate(s('bir iki üç'),s('bir dört')).wordEdits,2);
  assert.equal(evaluate(s('bir'),s('bir iki üç')).wer,2);
});
test('boundary metrics require explicit pairing and equal segment counts', () => {
  const a = parseSrt('1\n00:00:00,000 --> 00:00:02,000\nMerhaba.');
  const b = parseSrt('1\n00:00:00,500 --> 00:00:02,500\nMerhaba.');
  assert.equal(evaluate(a,b).boundaryMeanAbsoluteSeconds,null);
  assert.equal(evaluate(a,b,true).boundaryMeanAbsoluteSeconds,.5);
  assert.throws(()=>evaluate(a,[...b,...b],true));
  assert.throws(()=>parseSrt('not srt'));
});
