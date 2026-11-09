# Contributing to Wbskt.DotNet

How changes are written, committed and reviewed in this repo.

## Commit messages

Every commit subject, and every PR title, follows [Conventional Commits](https://www.conventionalcommits.org):

```
<type>(<scope>): <summary>

<body: why the change was needed, not a list of what changed>

Closes #N
```

- **type** is one of `feat`, `fix`, `perf`, `refactor`, `docs`, `test`, `chore`, `ci`, `build`.
- **scope** names the part of the code the change touches (client, storage, reconnect, buffer, protocol, conformance, release, ci).
  Leave it out, as in `docs: …`, when a change spans the repo.
- **summary** is lower case, in the imperative, with no full stop, and under about 72 characters:
  `fix(reconnect): honour Retry-After on a 503`.
- One logical change per commit. Squash fix-ups before review rather than leaving "fix review" commits.

PRs are merged by rebase, so each commit lands on `master` exactly as written. That is why the
commits, not only the PR title, follow the format.

## Branches and merging

- Never create a merge commit. Rebase onto `master` to catch up, and merge PRs with rebase (or squash).
- Never force-push a branch someone else owns or `master` itself.

## Pull requests

1. Find the GitHub issue(s) the change addresses and link them in the body (`Closes #N`, or `Refs #N`
   when it only does part of the work). Open an issue first if there is none.
2. Run the checks below before pushing.
3. **Review it again once it's open.** Read the full diff on GitHub as a fresh reviewer, not as the
   author: correctness and edge cases, concurrency, error handling, missing tests, and these
   conventions. Push fixes as follow-up commits, then add a line to the PR body:
   `Post-open review: <what was found and fixed, or "nothing found">`.
4. Wait for CI to pass, then ask for the merge.

## Checks before pushing

```bash
dotnet build Wbskt.slnx          # warnings are errors
dotnet test Wbskt.slnx           # unit tests, on net8.0 and net10.0

# the shared conformance suite, from a checkout of wbskt/Wbskt next to this one
dotnet build conformance/Wbskt.Conformance.Harness -c Release -o /tmp/harness
pip install -r ../Wbskt/Tests/Conformance/requirements.txt
cd ../Wbskt/Tests/Conformance
WBSKT_HARNESS="dotnet /tmp/harness/Wbskt.Conformance.Harness.dll" python -m pytest -v
```

The conformance tests come from wbskt/Wbskt `master`, so a protocol change merges there first.

## The protocol types

`src/Wbskt/Protocol` is this client's own copy of the wire types in
[Docs/Client.Protocol.md](https://github.com/wbskt/Wbskt/blob/master/Docs/Client.Protocol.md). The
platform keeps its own in wbskt/Wbskt (`Clients/Wbskt.Protocol`); the two are not shared, and the
conformance suite is what keeps them in step. Only the capability records a user builds are public.

## Releasing

The version is `SdkPackageVersion` in `Directory.Build.props`, in its NuGet spelling (`0.9.0-beta.1`).
Bump it in a PR, merge it, then tag `master` with `v` and the same version:

```bash
git tag v0.9.0-beta.1 && git push origin v0.9.0-beta.1
```

`release.yml` runs the checks again, refuses a tag that does not match the version or is not on
`master`, packs the package and pushes it to NuGet as `Wbskt` through trusted publishing. nuget.org
holds a policy trusting this repository and `release.yml`, the `NUGET_USER` secret names the
nuget.org account that owns the package, and the push runs in the `nuget` environment. No API key
is stored. A published version cannot be replaced, so a bad release is fixed by the next one.
