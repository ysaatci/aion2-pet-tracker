# Code signing with SignPath

Releases are signed for free by [SignPath.io](https://about.signpath.io) with a certificate from the
[SignPath Foundation](https://signpath.org). The repository is already prepared:

- `.github/workflows/ci.yml` tests and builds every push in GitHub Actions. SignPath only signs builds made by CI
  from this repository.
- `.github/workflows/release.yml` runs on a version tag. It builds the exe, submits it to SignPath, waits for your
  approval, and puts the signed exe in a **draft** release.
- `.signpath/artifact-configuration.xml` tells SignPath what to sign.
- The README has the **Code signing policy** and **Privacy** sections the Foundation requires.

Until SignPath is configured, the release workflow still works. It publishes an unsigned draft release.

## One-time setup (maintainer)

1. **Turn on two-factor authentication** for your GitHub account: Settings → Password and authentication. The
   Foundation requires MFA for everyone on the team, on GitHub and on SignPath.
2. **Apply** at <https://signpath.org/apply>. Use:
   - Repository: `https://github.com/ysaatci/aion2-pet-tracker`
   - Licence: GPL-3.0
   - Download page: `https://github.com/ysaatci/aion2-pet-tracker/releases`
   - Build system: GitHub Actions (`.github/workflows/release.yml`)

   Approval is manual and can take a few weeks. They check the repository, the README sections, and that releases
   are built by CI.
3. **After approval, in SignPath** (<https://app.signpath.io>):
   1. Organization → *Trusted build systems*: add the predefined **GitHub.com** system, then link it to the project.
      Optionally install the SignPath GitHub App for this repository.
   2. Project (for example slug `aion2-pet-tracker`) → *Artifact configurations* → new configuration, and paste
      `.signpath/artifact-configuration.xml`. Note its slug.
   3. The Foundation creates the **release-signing** signing policy. Make sure you are listed as an approver.
   4. *Users* → create a **CI user** and an API token for it, and give it submitter rights on the signing policy.
4. **In GitHub**, go to the repository → Settings → Secrets and variables → Actions:
   - Secret `SIGNPATH_API_TOKEN`: the CI user's token.
   - Variables:
     - `SIGNPATH_ORGANIZATION_ID`: shown in SignPath under Organization → Settings.
     - `SIGNPATH_PROJECT_SLUG`: for example `aion2-pet-tracker`.
     - `SIGNPATH_SIGNING_POLICY_SLUG`: for example `release-signing`.
     - `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`: the slug from step 3.2. Leave it empty to use the project default.

## Making a release

1. Bump `<Version>` in `src/AionMeter.App/AionMeter.App.csproj`, then commit and push.
2. Tag the release:
   ```
   git tag v0.1.1
   git push origin v0.1.1
   ```
3. The **Release** workflow builds and submits the exe. SignPath emails the approvers, so approve the request in
   SignPath within an hour (the workflow waits that long).
4. Open the draft release on GitHub, check the notes and the files, and click **Publish**.

The signed exe shows "SignPath Foundation" as its publisher. SmartScreen warnings fade as the certificate builds
reputation, and Smart App Control no longer blocks it.
