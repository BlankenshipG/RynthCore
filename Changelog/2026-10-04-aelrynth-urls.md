# Changelog — 2026-10-04 (docs/CI)

## fix: point dead tombohar URLs at aelrynth / sibling fork

- `installer/RynthCore.iss` `AppPublisherURL` → https://aelrynth.com/rynth.html
- `.github/workflows/release.yml` sibling clone → `github.com/${{ github.repository_owner }}/RynthSuite`
- Canonical public git remains https://aelrynth.com/git/RynthCore.git (see product page)

No engine/SDK version bump (docs and CI URL-only).
