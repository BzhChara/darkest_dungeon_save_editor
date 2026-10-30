# Project website and Google Search Console

The project website is a buildless static site. `docs/index.html` is the Chinese
homepage, `docs/en/index.html` is the English page, and `docs/assets/site.css` is
their shared stylesheet. Both pages work without JavaScript or external fonts.
The icon and favicon are copies of the editor's existing assets.

## Preview locally

From the repository root, with Python installed:

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory docs
```

Open <http://127.0.0.1:8765/>. Check the language links and the mobile layout.
The canonical and alternate-language URLs intentionally refer to the intended
public site, not localhost. Stop the server with Ctrl+C when finished.

## Publish with GitHub Pages

1. Commit and push the website files to the repository's `master` branch.
2. Open the repository's **Settings → Pages**.
3. Under **Build and deployment**, choose **Deploy from a branch**.
4. Select **master** and **/docs**, then save.
5. Wait for the Pages deployment to finish successfully.

The expected public site is:

<https://bzhchara.github.io/darkest_dungeon_save_editor/>

The `.nojekyll` file keeps the site as plain static files. Publishing from `/docs`
also exposes the other tracked files in that directory; the homepage links to
documentation on GitHub for readable Markdown. Ignored local files under
`docs/change-history/` and `.research/` must not be force-added or published.

To take the website offline, use **Unpublish site** in the Pages settings.
Reverting the website commit restores its source files without changing editor
code, releases, or user saves.

## Verify the website with Google

1. In Search Console, add a **URL-prefix** property for
   `https://bzhchara.github.io/darkest_dungeon_save_editor/`.
2. Choose **HTML file** verification. The included
   `googlef51b58cae32b32b6.html` is an unchanged copy of the file already downloaded
   by the repository owner. Confirm that the new property's instructions name
   this same file and content. If Google supplies a different file, place that
   exact file directly in `docs/` and publish it first.
3. Open the exact verification URL in an incognito window. For the included file:
   <https://bzhchara.github.io/darkest_dungeon_save_editor/googlef51b58cae32b32b6.html>.
   It must return the verification text, without requiring a login.
4. Click **Verify**. Keep the verification file deployed afterward.
5. Inspect the homepage URL and request indexing. In **Sitemaps**, submit
   `https://bzhchara.github.io/darkest_dungeon_save_editor/sitemap.xml`.

This property covers the project website, not the repository pages on
`github.com`. Verification and an indexing request do not guarantee inclusion in
Google results or a particular ranking.

There is intentionally no project-level `robots.txt`: crawlers read that file
from the hostname root, not from a repository subdirectory. The sitemap contains
only the two public landing pages, with reciprocal language alternatives.

## Maintain the site

Update both languages when changing features or installation instructions. Link
downloads to `/releases`, which includes prereleases, instead of `/releases/latest`
or a version-specific ZIP. Do not advertise unreleased source-only functionality
as part of a downloadable release.

If the domain or repository name changes, update canonical URLs, language
alternatives, Open Graph URLs, JSON-LD URLs and `sitemap.xml` together. Keep asset
and navigation paths relative so the pages also work under the repository prefix.

References:

- [GitHub Pages publishing source](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)
- [Google site ownership verification](https://support.google.com/webmasters/answer/9008080)
- [Requesting a crawl](https://developers.google.com/search/docs/crawling-indexing/ask-google-to-recrawl)
