"""Create the three WinGet manifests from an immutable published setup file."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('installer', type=Path)
parser.add_argument('--version', default='1.5.0')
parser.add_argument('--output', type=Path, default=Path('dist/winget'))
args = parser.parse_args()
package = 'SilviuVlasceanu.AnthropicProxyRSWin'
repo = 'https://github.com/silviuk/Anthropic-Proxy-RS-Win'
version = args.version
schema = '1.28.0'
digest = hashlib.sha256(args.installer.read_bytes()).hexdigest().upper()
folder = args.output / version
folder.mkdir(parents=True, exist_ok=True)
quote = json.dumps
version_text = f'''PackageIdentifier: {package}
PackageVersion: {quote(version)}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: {schema}
'''
locale_text = f'''PackageIdentifier: {package}
PackageVersion: {quote(version)}
PackageLocale: en-US
Publisher: Silviu Vlasceanu
PublisherUrl: https://github.com/silviuk
PublisherSupportUrl: {repo}/issues
PrivacyUrl: {repo}/blob/main/PRIVACY.md
Author: Ivan Zakutnii (m0n0x41d) and Silviu Vlasceanu
PackageName: Anthropic-Proxy-RS-Win
PackageUrl: {repo}
License: MIT
LicenseUrl: {repo}/blob/main/LICENSE
Copyright: Copyright (c) 2026 Silviu Vlasceanu; upstream copyright (c) 2025 Ivan Zakutnii
ShortDescription: Native Windows settings app for an Anthropic-to-OpenAI API proxy.
Description: Native Windows app with named settings profiles, key-safe configuration export, debug and verbose logs, and proxy control. Original proxy by Ivan Zakutnii (m0n0x41d), author of anthropic-proxy-rs. Windows UI and packaging by Silviu Vlasceanu.
Moniker: anthropic-proxy-rs-win
Tags:
- anthropic
- api
- developer-tools
- openai
- proxy
ReleaseNotesUrl: {repo}/releases/tag/windows-v{version}-1
ManifestType: defaultLocale
ManifestVersion: {schema}
'''
installer_text = f'''PackageIdentifier: {package}
PackageVersion: {quote(version)}
InstallerType: inno
Scope: user
MinimumOSVersion: 10.0.19041.0
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS
UpgradeBehavior: install
AppsAndFeaturesEntries:
- DisplayName: Anthropic-Proxy-RS-Win
  Publisher: Silviu Vlasceanu
  DisplayVersion: {quote(version)}
  ProductCode: '{{D9F06C1E-7714-4C6A-A5DE-315BD6D5AB54}}_is1'
Installers:
- Architecture: x64
  InstallerUrl: {repo}/releases/download/windows-v{version}-1/{args.installer.name}
  InstallerSha256: {digest}
ManifestType: installer
ManifestVersion: {schema}
'''
for kind, suffix, text in [('version', '', version_text), ('defaultLocale', '.locale.en-US', locale_text), ('installer', '.installer', installer_text)]:
    header = f'# yaml-language-server: $schema=https://aka.ms/winget-manifest.{kind}.{schema}.schema.json\n\n'
    (folder / f'{package}{suffix}.yaml').write_text(header + text, encoding='utf-8', newline='\r\n')
print(folder)
