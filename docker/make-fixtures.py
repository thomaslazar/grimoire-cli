#!/usr/bin/env python3
"""Generate fixture PDFs for the local Grimoire stack.

Uses PyMuPDF — the same library Grimoire reads PDFs with — so anything written
here is parseable by the indexer. Install with: sudo apt-get install -y python3-fitz
(the devcontainer image does this; rebuild the container if the import fails).

Usage: make-fixtures.py <path> <pages>
       make-fixtures.py --png <path>
"""
import sys

try:
    import fitz
except ImportError:
    sys.exit(
        "python3-fitz (PyMuPDF) is required to generate fixtures.\n"
        "  devcontainer: rebuild the container, or "
        "sudo apt-get install -y python3-fitz"
    )


def make_pdf(path: str, pages: int) -> None:
    doc = fitz.open()
    for i in range(pages):
        page = doc.new_page()
        page.insert_text((72, 72), f"grimoire-cli fixture — page {i + 1}")
    doc.save(path)
    doc.close()


def make_png(path: str) -> None:
    """A tiny valid PNG for the cover-upload smoke check.

    PyMuPDF is already a fixture dependency; Pillow is not installed in the
    devcontainer. The server decodes this with PIL.Image.verify(), so it has to
    be a real image, not bytes with a .png name.
    """
    pix = fitz.Pixmap(fitz.csRGB, fitz.IRect(0, 0, 16, 16))
    pix.clear_with(200)
    pix.save(path)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--png":
        make_png(sys.argv[2])
    elif len(sys.argv) == 3:
        make_pdf(sys.argv[1], int(sys.argv[2]))
    else:
        sys.exit("Usage: make-fixtures.py <path> <pages>\n       make-fixtures.py --png <path>")
