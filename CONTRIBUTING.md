<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Contributing to Heimdall

*Also available in French: [CONTRIBUTING.fr.md](CONTRIBUTING.fr.md).*

Contributions are welcome. This page is the front door: what to expect from the review, and the
handful of project conventions that are easy to miss from outside. The mechanics of building,
testing and naming things live in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md), and this page does
not repeat them.

## Licence and authorship

Heimdall is Apache 2.0. Under section 5 of that licence, anything you deliberately submit for
inclusion is licensed under the same terms, so there is no contributor agreement to sign and
nothing to send by mail.

Your commits stay yours. Pull requests are merged with a merge commit rather than squashed, so
your authorship survives in `git log` and in the repository's contributor graph, and contributors
are named in the changelog entry their work appears in.

New files carry the standard Apache 2.0 header. Put your own name in the copyright line of a file
you wrote.

## Before you open a pull request

```powershell
powershell -File Build.ps1 -Mode Debug
```

That one command is the gate: it runs the tests, checks formatting with
`dotnet format --verify-no-changes`, builds, and publishes. Warnings are errors project-wide, so a
build that prints a warning does not pass. If it reaches `[5/5] Published`, CI will agree with you.

`Build.ps1` rewrites the version metadata in `src/Heimdall.App/Heimdall.App.csproj` before
building. Restore that file before committing if you did not mean to bump the version.

## What the review looks for

**A change is expected to come with a test that fails without it.** Not a test that exercises the
code, a test that would have caught the bug. The usual way to show this is to break the fix on
purpose, watch the new test go red, and put that back: several test files in this repository
describe the mutant they were proved against, and a pull request that says which one it used is
reviewed faster.

**The commit subject says what changed, not what was typed.** Subjects are lowercase statements
about behaviour, with a type and a scope:

```
fix(ssh): the host key prompt accepted a key it had refused
feat(tree): a folder can be dropped between two others
docs: the interface is trilingual, and the key count is measured
```

The body explains why, and what was measured. A branch is named the same way,
`<type>/<short-kebab-description>`, after the change and never after a tool or a person. The full
vocabulary is in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

**No user-facing string is written in code.** Every sentence a user can read belongs in the locale
catalogues under `locales/`, reached through `{loc:Translate Key}` in XAML or the localizer in C#.
Keys are CamelCase and named by context: `ErrorPlinkNotFound`, `BtnConnect`.

**Every catalogue holds the same keys.** `LocaleCatalogueParityTests` enforces this in both
directions and across placeholders: a key added to English alone reaches the other languages as
the key name itself, in place of the sentence.

## Characters that are refused

This is the convention most likely to surprise a first contribution, because a text editor
produces the refused characters without being asked.

| Refused | Write instead |
|---|---|
| em dash, en dash, unicode hyphen, minus sign | `-` |
| curly quotes, low quotes, French or Spanish guillemets | `"` |
| curly apostrophe | `'` |
| single-character ellipsis | `...` |
| no-break, narrow and thin spaces, zero-width space, byte order mark | a plain space, or nothing |
| oe and ae ligatures | `oe`, `ae` |

A plain ASCII character says the same thing and survives a Windows terminal, a diff, a console
code page and a CI log. The rule holds in code, in comments, in documentation, in commit messages
and in the locale catalogues, in every language.

**Accents are not typography.** Every accented letter of French and Spanish is welcome, as are
arrows, box-drawing characters, ballot boxes and emoji where they carry meaning. What is refused
is the typographic substitute for a character ASCII already has.

`SourceTypographyGuardTests` and `DocumentationTypographyGuardTests` enforce this, so a slip
fails the build rather than reaching a release.

## Documentation comes in two languages

Public documents are versioned and mirrored: English at its usual path, French beside it
(`README.md` and `README.fr.md`, `docs/X.md` and `docs/fr/X.md`). A change to one is not finished
until the other says the same thing. The changelog and the published release notes are the
exception and stay English only.

If you are comfortable in only one of the two languages, write that side and say so in the pull
request. A missing mirror is a small piece of work for the maintainer; a wrong one is not.

## Adding a language

The interface ships in English, French and Spanish. A fourth language touches these places, and
the guards will tell you if you miss one:

- [ ] `locales/<code>.json`, holding exactly the keys `locales/en.json` holds, in the same order
      and at the same line layout, so the catalogues can be read side by side
- [ ] the `Content` copy in `src/Heimdall.App/Heimdall.App.csproj`
- [ ] a `ComboBoxItem` in the language list in `src/Heimdall.App/MainWindow.xaml`, carrying the
      code in `Tag`
- [ ] `ValidLocales` in `src/Heimdall.Core/Configuration/SchemaValidator.cs`
- [ ] `SupportedCultures` on the localization bridge in
      `src/Heimdall.App/Services/TwinShellBootstrapper.cs`
- [ ] the mojibake allow-list in `tests/Heimdall.Core.Tests/LocaleMojibakeGuardTests.cs`, if the
      language uses letters no shipped language uses
- [ ] `THIRD-PARTY-NOTICES.<code>.md`, and the cross-links at the top of its siblings

Two things a new language does not reach today, and neither is a blocker: the embedded draw.io
editor ships English and French resources only, and the passphrase generator's word lists are
English and French.

## Reporting something instead

A bug report that names the version, says what was expected and what happened, and carries the
relevant lines from the log is worth more than a patch that guesses. For anything with a security
dimension, read [SECURITY.md](SECURITY.md) first and do not open a public issue.

## How the review goes

Expect the maintainer to push follow-up commits on top of a contribution rather than send it back
through several rounds of review. When that happens the commits are separate from yours, each one
says what it changes and why, and the pull request explains the reasoning. If you would rather
make those changes yourself, say so and the review will wait for you.
