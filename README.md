# aspnetzero-blazor-telerik

A [Claude Code](https://claude.com/claude-code) Agent Skill for adding, converting, and reviewing
**Blazor UI in ASP.NET Zero (ANZ) / ASP.NET Boilerplate (ABP)** solutions using **Telerik UI for
Blazor**, on .NET 10+.

ASP.NET Zero ships official web UIs for MVC & jQuery, Angular, and React — there is no first-party
Blazor Server/WASM web UI (ANZ's "Blazor" offering is the MAUI Blazor Hybrid mobile app, a different
thing). This skill exists because wiring Blazor into the ANZ host is a graft, not a template: static
web assets, middleware order, circuit-scoped DI, unit-of-work boundaries, `AbpSession` over SignalR,
`TelerikRootComponent`, and Metronic theme conflicts all have to be gotten right by hand, and they fail
in ways that look like Blazor bugs but are actually ABP lifetime-mismatch bugs.

## What it covers

- The full vertical slice: entity → EF config → migration → `AppPermissions` → localization →
  ABP `AppService` + DTOs → Razor component → menu.
- The Blazor-into-ANZ integration seams: hosting model detection (legacy Blazor Server graft vs.
  unified Blazor Web App vs. WASM), static asset wiring, DI/circuit scope, `AbpSession`, Mapperly vs.
  AutoMapper mapping (ANZ 15.2+ migrated to Mapperly).
- Telerik UI for Blazor grid/form patterns wired to ABP `AppService` paging/sorting/filtering
  conventions.

## Contents

| Path | Purpose |
|---|---|
| [`SKILL.md`](SKILL.md) | The skill definition Claude Code loads — when to use it, and the step-by-step approach. |
| [`references/integration-setup.md`](references/integration-setup.md) | Host wiring: static assets, middleware order, hosting model detection. |
| [`references/abp-backend.md`](references/abp-backend.md) | Entities, `AppService`, DTOs, permissions, localization, mapping. |
| [`references/telerik-patterns.md`](references/telerik-patterns.md) | Telerik grid/form component patterns and gotchas. |
| [`references/checklist.md`](references/checklist.md) | Pre-flight / review checklist for a Blazor+Telerik change in an ANZ solution. |
| [`assets/templates/`](assets/templates/) | Copy-paste starting points: entity, DTOs, `AppService` + interface, grid request mapper, scoped executor, Telerik list/edit `.razor` components. |
| [`llms.txt`](llms.txt) | Machine-readable index of this repo for LLM agents/crawlers. |

## How to use it

Claude Code discovers **Agent Skills** from a `SKILL.md` file inside a named folder under a skills
directory. This repo *is* that folder (`aspnetzero-blazor-telerik/`), so installing it is just placing
it in one of those directories:

- **Personal, all projects:** clone (or copy the extracted `aspnetzero-blazor-telerik/` folder) into
  `~/.claude/skills/aspnetzero-blazor-telerik/`.
- **Project-scoped, checked into a repo:** copy the folder into
  `<your-project>/.claude/skills/aspnetzero-blazor-telerik/`.

```bash
git clone https://github.com/qmmughal/aspnetzero-blazor-telerik-skill.git \
  ~/.claude/skills/aspnetzero-blazor-telerik
```

Once the folder is in place, Claude Code reads the YAML frontmatter in `SKILL.md` (`name` +
`description`) to decide when the skill is relevant — no manual invocation needed. It activates
automatically for prompts like:

- "Add a Blazor page to my ASP.NET Zero project"
- "Convert this DataTables/jQuery page to Blazor"
- "Wire a TelerikGrid to my AppService"
- "`blazor.server.js` 404" / "`AbpSession.TenantId` is null in my component"
- Any `.razor` work in a Zero/ABP solution

### Direct download

[`aspnetzero-blazor-telerik.skill`](aspnetzero-blazor-telerik.skill) is the original packaged file —
a zip of this same skill folder, kept in the repo for direct download/install without cloning. To use
it:

```bash
curl -LO https://github.com/qmmughal/aspnetzero-blazor-telerik-skill/raw/main/aspnetzero-blazor-telerik.skill
unzip aspnetzero-blazor-telerik.skill -d ~/.claude/skills/
```

That extracts straight to `~/.claude/skills/aspnetzero-blazor-telerik/` (the zip's top-level entry is
already named `aspnetzero-blazor-telerik/`). Swap the destination for
`<your-project>/.claude/skills/` for a project-scoped install instead.

## License

[MIT](LICENSE)
