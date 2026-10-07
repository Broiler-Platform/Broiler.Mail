import assert from 'node:assert/strict';
import test from 'node:test';
import { chooseVersion, versionsFromTags } from './resolve-preview-version.mjs';

test('a plain release line starts at preview.1 and increments numerically', () => {
  assert.equal(chooseVersion('2.0.0', []), '2.0.0-preview.1');
  assert.equal(chooseVersion('2.0.0', ['2.0.0-preview.1']), '2.0.0-preview.2');
  assert.equal(chooseVersion('2.0.0', ['2.0.0-preview.9', '2.0.0-preview.10']), '2.0.0-preview.11');
});

test('a configured preview is a floor and other release lines do not affect it', () => {
  assert.equal(chooseVersion('2.0.0-preview.4', [
    '2.0.0-preview.1', '2.1.0-preview.99', '2.0.0', '2.0.0-rc.9',
  ]), '2.0.0-preview.4');
  assert.equal(chooseVersion('2.0.0-preview.4', ['2.0.0-preview.4']), '2.0.0-preview.5');
});

test('only unused previews on the configured release line are accepted', () => {
  const published = ['2.0.0-preview.1'];
  assert.equal(chooseVersion('2.0.0', published, { suffix: 'preview.3' }), '2.0.0-preview.3');
  assert.equal(chooseVersion('2.0.0', published, { suffix: 'preview.2' }), '2.0.0-preview.2');
  for (const suffix of ['preview.1', 'rc.2', 'preview.0', 'preview.02', 'preview.2;evil']) {
    assert.throws(() => chooseVersion('2.0.0', published, { suffix }));
  }
  for (const configured of ['2.0', '2.0.0-rc.1', '2.0.0-preview.0', 'v2.0.0']) {
    assert.throws(() => chooseVersion(configured, published));
  }
});

test('only mail-v tags count as earlier versions', () => {
  const tags = ['mail-v2.0.0-preview.4', 'v2.0.0-preview.9', 'plate-v2.0.0-preview.7', ' mail-v2.0.0-preview.5\n'];
  assert.deepEqual(versionsFromTags(tags), ['2.0.0-preview.4', '2.0.0-preview.5']);
  assert.equal(chooseVersion('2.0.0', versionsFromTags(tags)), '2.0.0-preview.6');
});
