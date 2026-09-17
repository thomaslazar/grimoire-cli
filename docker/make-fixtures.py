#!/usr/bin/env python3
"""Generate fixture PDFs for the local Grimoire stack.

Uses PyMuPDF — the same library Grimoire reads PDFs with — so anything written
here is parseable by the indexer. Install with: sudo apt-get install -y python3-fitz
(the devcontainer image does this; rebuild the container if the import fails).

Usage: make-fixtures.py <path> <pages>
       make-fixtures.py --png <path>
       make-fixtures.py --stl <path>
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


def make_stl(path: str) -> None:
    """A one-triangle binary STL.

    Grimoire renders thumbnails for .stl only (backend/indexer/models3d.py), and
    reads the triangle count straight out of the header — so a single real
    triangle is enough to index, count and render. Written with struct rather
    than a mesh library: none is installed, and the format is 84 bytes of header
    plus 50 per facet.
    """
    import struct

    header = b"grimoire-cli fixture".ljust(80, b"\0")
    triangle = struct.pack(
        "<12fH",
        0.0, 0.0, 1.0,   # normal
        0.0, 0.0, 0.0,   # vertex 1
        1.0, 0.0, 0.0,   # vertex 2
        0.0, 1.0, 0.0,   # vertex 3
        0,               # attribute byte count
    )
    with open(path, "wb") as fh:
        fh.write(header + struct.pack("<I", 1) + triangle)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--png":
        make_png(sys.argv[2])
    elif len(sys.argv) == 3 and sys.argv[1] == "--stl":
        make_stl(sys.argv[2])
    elif len(sys.argv) == 3:
        make_pdf(sys.argv[1], int(sys.argv[2]))
    else:
        sys.exit(
            "Usage: make-fixtures.py <path> <pages>\n"
            "       make-fixtures.py --png <path>\n"
            "       make-fixtures.py --stl <path>"
        )
