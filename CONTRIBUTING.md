# Contributing to Talesmith

Thanks for helping. The [contributor guide](https://talesmith.dev/developers/contributing) has the conventions for code, tests, commits
and documentation; this page is the short version.

## Before you start

- For a bug, open an issue with the steps to reproduce it. For a security problem, follow [SECURITY.md](SECURITY.md) instead.
- For a larger change, open an issue first, so the approach is agreed on before you write the code.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Build and test:

   ```bash
   dotnet build Talesmith.slnx -warnaserror
   dotnet test --solution Talesmith.slnx -- --filter-not-trait "Category=Slow"
   ```

3. Keep the formatting: `dotnet format whitespace Talesmith.slnx` and `dotnet format style Talesmith.slnx` fix it.
4. When behavior, file formats or APIs change, update the website in the same commit; `website/STYLE.md` covers how it is written.
5. Write each commit message as a [Conventional Commit](https://www.conventionalcommits.org/en/v1.0.0/), such as
   `fix(editor): keep the selection when a scene reloads`.
6. Open a pull request against `main` and fill in the template.

## License

Talesmith is licensed under the [Apache License 2.0](LICENSE). Contributions you submit are licensed under the same terms, as section 5
of the license describes.
