# Enterprise Edition development

ForgeDeck mirrors GitLab's CE/EE separation:

- Put proprietary product code only under `/ee`
- Guard EE capabilities with `ICapabilityService` / signed entitlements
- Without a licence, EE builds must behave like CE for gated features
- Force CE with `FOSS_ONLY=1` or by omitting `-p:IncludeEE=true`

See `/ee/README.md` and `LICENSE.md`.
