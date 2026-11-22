#!/usr/bin/env python3
"""用最新发布目录重建 Windows zip 与 Linux tar.gz（Launcher 根文件沿用现有包）。

用法: python build_packages.py [--win-pub path] [--linux-pub path]
"""

import copy
import io
import os
import shutil
import tarfile
import zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(ROOT)
WIN_PUB = os.path.join(ART, "windows", "pub7")
LINUX_PUB = os.path.join(ART, "linux", "pub7")
WIN_ZIP = os.path.join(ART, "ClassBar-Windows-x64.zip")
LINUX_TAR = os.path.join(ART, "ClassBar-Linux-x64.tar.gz")
APP_DIR = "app-2.1.0"
APP_CURRENT = b"app-2.1.0"
EXCLUDE = {"Config", "Profiles", "Logs", "Cache", "Backups", "Temp"}


def app_files(pub):
    out = []
    for base, dirs, files in os.walk(pub):
        dirs[:] = sorted(d for d in dirs if d not in EXCLUDE)
        for f in sorted(files):
            full = os.path.join(base, f)
            rel = os.path.relpath(full, pub).replace(os.sep, "/")
            out.append((rel, full))
    return out


def linux_mode(rel):
    if rel in ("ClassIsland.Desktop", "createdump"):
        return 0o755
    base = os.path.basename(rel)
    if base.endswith(".so") or ".so." in base:
        return 0o755
    return 0o644


def rebuild_zip():
    files = app_files(WIN_PUB)
    assert os.path.getsize(os.path.join(WIN_PUB, "ClassIsland.dll")) > 1_000_000

    tmp = WIN_ZIP + ".tmp"
    with zipfile.ZipFile(WIN_ZIP) as zin:
        keep = [(i, zin.read(i.filename)) for i in zin.infolist()
                if not i.filename.startswith(APP_DIR + "/")]

    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zout:
        for info, data in keep:
            zout.writestr(copy.copy(info), data)

        # 目录条目
        dirs = {APP_DIR}
        for rel, _ in files:
            parts = rel.split("/")[:-1]
            for i in range(1, len(parts) + 1):
                dirs.add(APP_DIR + "/" + "/".join(parts[:i]))
        for d in sorted(dirs):
            zout.writestr(d + "/", b"")

        zout.writestr(APP_DIR + "/.current", APP_CURRENT)
        for rel, full in files:
            with open(full, "rb") as fh:
                zout.writestr(APP_DIR + "/" + rel, fh.read())

    os.replace(tmp, WIN_ZIP)
    with zipfile.ZipFile(WIN_ZIP) as z:
        names = z.namelist()
        print("WIN zip:", len(names), "entries,", round(os.path.getsize(WIN_ZIP) / 1048576, 1), "MB")
        print("  runtimes:", [n for n in names if "/runtimes/" in n])
        print("  dll:", z.getinfo(APP_DIR + "/ClassIsland.dll").file_size)


def rebuild_tar():
    files = app_files(LINUX_PUB)
    assert os.path.getsize(os.path.join(LINUX_PUB, "ClassIsland.dll")) > 1_000_000

    tmp = LINUX_TAR + ".tmp"
    with tarfile.open(LINUX_TAR, "r:gz") as tin, tarfile.open(tmp, "w:gz", compresslevel=6) as tout:
        for m in tin.getmembers():
            arc = m.name[2:] if m.name.startswith("./") else m.name
            if arc.startswith(APP_DIR + "/") or arc == APP_DIR:
                continue
            if m.isfile():
                tout.addfile(copy.copy(m), tin.extractfile(m))
            else:
                tout.addfile(copy.copy(m))

        def add_dir(name):
            ti = tarfile.TarInfo("./" + name)
            ti.type = tarfile.DIRTYPE
            ti.mode = 0o755
            ti.uid = ti.gid = 0
            ti.uname = ti.gname = "root"
            ti.mtime = 1774100000
            tout.addfile(ti)

        add_dir(APP_DIR)
        seen = set()
        for rel, _ in files:
            parts = rel.split("/")[:-1]
            for i in range(1, len(parts) + 1):
                d = "/".join(parts[:i])
                if d not in seen:
                    seen.add(d)
                    add_dir(APP_DIR + "/" + d)

        ti = tarfile.TarInfo("./" + APP_DIR + "/.current")
        ti.size = len(APP_CURRENT)
        ti.mode = 0o644
        ti.uid = ti.gid = 0
        ti.uname = ti.gname = "root"
        ti.mtime = 1774100000
        tout.addfile(ti, io.BytesIO(APP_CURRENT))

        for rel, full in files:
            with open(full, "rb") as fh:
                data = fh.read()
            ti = tarfile.TarInfo("./" + APP_DIR + "/" + rel)
            ti.size = len(data)
            ti.mode = linux_mode(rel)
            ti.uid = ti.gid = 0
            ti.uname = ti.gname = "root"
            ti.mtime = 1774100000
            tout.addfile(ti, io.BytesIO(data))

    os.replace(tmp, LINUX_TAR)
    with tarfile.open(LINUX_TAR, "r:gz") as t:
        ms = t.getmembers()
        print("LINUX tar:", len(ms), "entries,", round(os.path.getsize(LINUX_TAR) / 1048576, 1), "MB")
        print("  runtimes:", [(m.name, oct(m.mode)) for m in ms if "/runtimes/" in m.name])
        print("  exe:", [(m.name, oct(m.mode)) for m in ms
                         if m.name in ("./ClassIsland", "./ClassIsland.Launcher", "./app-2.1.0/ClassIsland.Desktop")])


if __name__ == "__main__":
    rebuild_zip()
    rebuild_tar()
