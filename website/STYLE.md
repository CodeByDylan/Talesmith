# Writing style

How pages on this site are written. `npm run lint:prose` checks the mechanical rules; the rest is up to you and the reviewer.

## Voice and tone

- Write to the reader as "you". Say what to do and what happens: "Press Ctrl+P to play the scene. The app bar turns the accent color."
- Plain and direct. Short sentences, common words, one idea per sentence where you can.
- Describe; do not sell. No adjectives that only praise ("powerful", "blazing fast", "beautiful"). If something is fast, give the number.
- Be exact. Name the menu item, the field, the key, the file. Use the labels the editor shows, in bold: **File › Build settings…**,
  the **Inspector**, the **Snap** toggle.
- Present tense, active voice. "The editor compiles the script", not "the script will be compiled".
- Explain why only when the reader needs it to make a choice.
- No filler openings ("In this page we will…", "Let's…") and no summaries that repeat the page.

## Punctuation and characters

The lint rejects these in `content/`, `src/data/`, `src/pages/`, `src/components/home/` and screenshot alt text:

- The em dash. Use a comma, a colon, parentheses or two sentences.
- The en dash as a dash. It is only allowed between numbers in a range, such as 1–5.
- ` -- ` as a dash.
- Emoji.
- Stock phrases: delve, seamless, leverage, robust, it's worth noting, in conclusion, unlock the power, elevate, game-changer,
  dive into, deep dive, in today's, whether you're, empower, supercharge, cutting-edge, effortless, harness the, look no further.

Code blocks and inline code are not checked.

Use › between menu levels (**Window › Layout**), × for sizes (128 × 148), and … only where the editor's own label has it.

## Icons

Icons appear where the layout uses them: cards in a `CardGrid` and the homepage. Never put icons or emoji in headings or running text.

## Headings

- Sentence case: "Painting tools", "Your first plugin", not "Painting Tools".
- The page title comes from the front matter; start the body with a paragraph, not a heading.
- Use `##` for sections and `###` for subsections. Do not go deeper.
- Headings name the task or the thing ("Slice a sprite sheet", "Version ranges"), not a question.

## Structure of a page

1. **What it is.** The opening paragraph says what the feature is and what the page covers, in two to four sentences. It is shown
   larger than body text, so keep it tight.
2. **How to do it.** The common task first, as numbered steps (`<Steps>`) with a screenshot where something visible happens.
3. **Details and reference.** Options, fields and edge cases, often as a table.
4. **Related.** A short list or `CardGrid` of links to related pages, when there are natural next steps.

Tutorials (Your first game, Your first script, Your first plugin) are steps from start to finish and must work exactly as written.
Reference pages (file formats, extension points, shortcuts) are tables first and prose second.

Keep pages focused. If a page grows past one topic, split it and add the new page to the sidebar.

## Front matter

Every page has:

```yaml
---
title: Painting tools
description: One sentence for search results and link previews, ending with a period.
---
```

Add `sidebar_label` only when the sidebar needs a shorter title.

## Code samples

- C# follows the repository's style: file-scoped namespaces, four-space indentation, `var` where the type is obvious, expression
  bodies for one-liners, `_camelCase` private fields, braces on their own lines.
- Samples must compile against the current API. Check names against the source (`src/Talesmith.Scripting/Script.cs` for scripts,
  the `ServiceCollection` extension methods for plugins) or the samples in `samples/`, and prefer adapting code that already runs in a
  sample or test.
- Show complete files for tutorials, with a `title="assets/scripts/Coin.cs"` on the code block. Shorter snippets are fine elsewhere
  if the surrounding code is obvious.
- Use `csharp`, `json`, `xml`, `bash` and `glsl` as languages. Shell commands are bash and run from the repository root unless
  the text says otherwise.
- Comments in code samples follow the repository rule: only what the code cannot say, one line.

## Screenshots

- Every screenshot comes from `tools/Talesmith.Screenshots`, as
  [Adding a documentation screenshot](content/developers/tools.mdx#adding-a-documentation-screenshot) describes. Never add hand-made
  screenshots.
- Show the editor in the state the text describes, with realistic content (the templates and samples), not empty panels.
- Crop to the relevant panel or dialog with the scene's `Region` when the whole window would make the detail too small.
- Write alt text that describes what the image shows, not what it is for. Register a good default with the screenshot and only pass
  `alt` on a page when the context needs something different.
- Captions are optional and short. Use them when the image needs a pointer ("The terrain rule for coast corners").
- One screenshot per idea. Do not stack several screenshots without text between them.

## Linking

- Within a section, link to the file with a relative path: `[Prefabs](./prefabs.mdx)`.
- Across sections, link to the route: `[Your first script](/scripting/scripts/first-script)`.
- Link the first mention of a concept that has its own page, not every mention.
- Link text says where it goes ("see [Terrains](./terrains.mdx)"), never "click here".
- External links only to stable, official pages (.NET, Avalonia, Docusaurus).

## Terms

| Use | Not |
| --- | --- |
| entity | object, game object (except the **GameObject** menu) |
| component | behaviour, behavior |
| script | behaviour |
| scene | level (unless you mean a level of a game) |
| tile map, map | tilemap |
| play mode | runtime mode |
| the editor | the IDE, the app |
| plugin | mod, extension (except for "extension points") |
| Windows and Linux | Win |
