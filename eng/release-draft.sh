#!/usr/bin/env bash
# Creates the draft GitHub pre-release for one Broiler.Mail version.
#
#   eng/release-draft.sh <version> <artifacts-dir>
#
# <artifacts-dir> holds the packages scripts/Publish-Windows.ps1 wrote in the Publish
# workflow, downloaded side by side (actions/download-artifact with merge-multiple):
#
#   Broiler.Mail-<version>-<runtime>-<variant>.zip   (+ .zip.sha256)
#
# for <runtime> win-x64 and win-arm64 and <variant> self-contained and framework-dependent.
# Every one of the eight files must be there; they are attached as they are.
#
# The tag mail-v<version> must already exist on the remote; the release is created for it
# as a draft, so nothing is public until someone publishes it. Needs the GitHub CLI with
# GH_TOKEN (or a login) that may write releases.
set -euo pipefail

version=${1:?usage: eng/release-draft.sh <version> <artifacts-dir>}
artifacts=${2:?usage: eng/release-draft.sh <version> <artifacts-dir>}
tag="mail-v$version"
out=$(mktemp -d)

assets=()
for runtime in win-x64 win-arm64; do
  for variant in self-contained framework-dependent; do
    zip="$artifacts/Broiler.Mail-$version-$runtime-$variant.zip"
    for f in "$zip" "$zip.sha256"; do
      [ -f "$f" ] || { echo "missing $f" >&2; exit 1; }
      assets+=("$f")
    done
  done
done

cat > "$out/notes.md" <<EOF
Broiler Mail **$version**, an unsigned preview build for evaluation and testing, not for
production use. The HTML preview is not yet process-isolated (see
\`docs/html-renderer-security.md\`); treat this build as a validation package.

## Downloads

| Platform | File | Run |
| --- | --- | --- |
| Windows x64 | \`Broiler.Mail-$version-win-x64-self-contained.zip\` | unzip, start \`Broiler.Mail.Windows.exe\` |
| Windows x64 | \`Broiler.Mail-$version-win-x64-framework-dependent.zip\` | install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0), unzip, start \`Broiler.Mail.Windows.exe\` |
| Windows ARM64 | \`Broiler.Mail-$version-win-arm64-self-contained.zip\` | unzip, start \`Broiler.Mail.Windows.exe\` |
| Windows ARM64 | \`Broiler.Mail-$version-win-arm64-framework-dependent.zip\` | install the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0), unzip, start \`Broiler.Mail.Windows.exe\` |

The self-contained packages carry a NativeAOT executable: no .NET runtime to install. The
framework-dependent packages are the smaller download for machines that already have the
.NET 10 runtime. Each zip has a \`.sha256\` beside it, and carries \`START-HERE.md\`,
\`build-manifest.json\` and the third-party notices. A checksum is not a signature: the
executables are not code-signed, so Windows SmartScreen may ask before the first start.
EOF

gh release create "$tag" "${assets[@]}" \
  --verify-tag \
  --draft \
  --prerelease \
  --title "Broiler Mail $version" \
  --notes-file "$out/notes.md" \
  --generate-notes
