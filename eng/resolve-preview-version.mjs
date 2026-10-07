// Chooses the next Broiler.Mail preview version for the Publish workflow.
//
// The workflow does not push packages anywhere, so the record of what has been built is the
// repository's tags: every successful run tags its commit mail-v<version>. The configured
// version, BroilerMailVersion from Directory.Build.props, names the release line - either a
// plain X.Y.Z (previews of it start at preview.1) or X.Y.Z-preview.N (N is the floor). The
// next version is raised past every tagged preview on the same line.
import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));

export const tagPrefix = 'mail-v';

function parsePreview(version) {
  const match = /^(\d+\.\d+\.\d+)-preview\.([1-9]\d*)$/.exec(version);
  if (!match) throw new Error(`Only X.Y.Z-preview.N versions (N >= 1) may be published: '${version}'.`);
  return { prefix: match[1], number: BigInt(match[2]) };
}

function parseConfigured(version) {
  if (/^\d+\.\d+\.\d+$/.test(version)) return { prefix: version, number: 1n };
  return parsePreview(version);
}

export function chooseVersion(configured, published, { suffix = '' } = {}) {
  const { prefix, number: floor } = parseConfigured(configured);
  let next = floor;
  for (const version of published) {
    const match = /^(\d+\.\d+\.\d+)-preview\.([1-9]\d*)$/i.exec(version);
    if (match && match[1] === prefix && BigInt(match[2]) >= next) {
      next = BigInt(match[2]) + 1n;
    }
  }

  const automatic = `${prefix}-preview.${next}`;
  const requested = suffix ? `${prefix}-${suffix}` : automatic;
  const preview = parsePreview(requested);
  if (preview.prefix !== prefix || preview.number < next) {
    throw new Error(`Requested '${requested}' must use ${prefix} and be at least '${automatic}'.`);
  }
  return requested;
}

// Tags that are not ours (other prefixes, other products) are ignored rather than rejected.
export function versionsFromTags(tags) {
  return tags
    .map(tag => tag.trim())
    .filter(tag => tag.startsWith(tagPrefix))
    .map(tag => tag.slice(tagPrefix.length));
}

function git(...args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8' });
}

function readConfiguredVersion() {
  const output = execFileSync('dotnet', [
    'msbuild', 'src/Broiler.Mail.Core/Broiler.Mail.Core.csproj', '-nologo',
    '-getProperty:BroilerMailVersion',
  ], { cwd: root, encoding: 'utf8' });
  return output.trim();
}

function main() {
  const configured = readConfiguredVersion();
  parseConfigured(configured);
  // A shallow checkout carries no tags; ask the remote rather than trusting the clone.
  const tags = git('ls-remote', '--tags', '--refs', 'origin', `${tagPrefix}*`)
    .split('\n')
    .filter(Boolean)
    .map(line => line.split('\t')[1].replace(/^refs\/tags\//, ''));
  const version = chooseVersion(configured, versionsFromTags(tags), {
    suffix: process.env.VERSION_SUFFIX || '',
  });
  console.log(`Version: ${version} (${tags.length} earlier tags)`);
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT,
      `version=${version}\ntag=${tagPrefix}${version}\n`);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
