#!/usr/bin/env python3
"""构造 ClassBar 的 amd64 deb 安装包（无需 dpkg-deb）。"""

import hashlib
import io
import os
import shutil
import tarfile
import time

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "linux_root")
STAGE = os.path.join(ROOT, "deb_stage")
OUT = os.path.join(os.path.dirname(ROOT), "ClassBar-2.1.0-amd64.deb")

PKG = "classbar"
VERSION = "2.1.0"
ARCH = "amd64"
INSTALL_DIR = "opt/classbar"
EPOCH = int(os.environ.get("SOURCE_DATE_EPOCH", "1774100000"))

# 依赖只写「跨发行版同名且必定存在」的包名，并且不写多候选（a | b）。
# 原因：Debian 10/UOS/Deepin 20 叫 libgcc1，Debian 11+ 叫 libgcc-s1；ICU 版本号各发行版不同
# （libicu63/66/70/72/74），OpenSSL 有 libssl1.1/libssl3 两代。带版本号的硬依赖或
# "a | b" 多候选会让国内发行版的图形化安装器（深度/UOS 安装器）直接判定依赖缺失而拒绝安装。
# 这些库在运行时由 .NET/Avalonia 动态探测版本并 dlopen，不声明也不会缺库；
# libgcc_s.so.1 由 libstdc++6 传递依赖带上，OpenSSL/ICU 在任何有 apt 的桌面上必然存在。
DEPENDS = (
    "libc6, libstdc++6, zlib1g, libfontconfig1, libfreetype6, "
    "libx11-6, libice6, libsm6, libasound2, libpulse0, "
    "ca-certificates, tzdata"
)

DESKTOP = """[Desktop Entry]
Type=Application
Version=1.0
Name=ClassBar
Name[zh_CN]=ClassBar
GenericName=\u8bfe\u8868\u663e\u793a\u5de5\u5177
Comment=\u5728\u5c4f\u5e55\u9876\u90e8\u5e38\u9a7b\u663e\u793a\u8bfe\u8868\u3001\u65f6\u95f4\u4e0e\u5012\u8ba1\u65f6
Comment[zh_CN]=\u5728\u5c4f\u5e55\u9876\u90e8\u5e38\u9a7b\u663e\u793a\u8bfe\u8868\u3001\u65f6\u95f4\u4e0e\u5012\u8ba1\u65f6
Exec=/opt/classbar/ClassIsland
Icon=classbar
Terminal=false
Categories=Education;Office;Utility;
StartupWMClass=ClassIsland
StartupNotify=false
"""

POSTINST = """#!/bin/sh
set -e

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi

exit 0
"""

POSTRM = """#!/bin/sh
set -e

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi

exit 0
"""


def norm_tarinfo(ti, mode, is_dir=False, is_link=False):
    ti.uid = 0
    ti.gid = 0
    ti.uname = "root"
    ti.gname = "root"
    ti.mtime = EPOCH
    ti.mode = mode
    ti.type = tarfile.DIRTYPE if is_dir else (tarfile.SYMTYPE if is_link else tarfile.REGTYPE)
    return ti


def file_mode(rel):
    """Windows 上解压会丢失可执行位，这里按文件名强制设定。"""
    exe = {
        "opt/classbar/ClassIsland",
        "opt/classbar/ClassIsland.Launcher",
        "opt/classbar/app-2.1.0/ClassIsland.Desktop",
        "opt/classbar/app-2.1.0/createdump",
    }
    if rel in exe:
        return 0o755
    base = os.path.basename(rel)
    if base.endswith(".so") or ".so." in base:
        return 0o755
    return 0o644


def make_icons():
    """从 384px 源图生成 hicolor 多尺寸图标。"""
    from PIL import Image

    assets = os.path.join(SRC, "app-2.1.0", "Assets")
    src = os.path.join(assets, "AppLogo@384w.png")
    if not os.path.exists(src):
        src = os.path.join(assets, "AppLogo.png")

    out = []
    with Image.open(src) as im:
        im = im.convert("RGBA")
        for size in (16, 24, 32, 48, 64, 128, 256):
            buf = io.BytesIO()
            im.resize((size, size), Image.LANCZOS).save(buf, format="PNG", optimize=True)
            out.append(("usr/share/icons/hicolor/%dx%d/apps/classbar.png" % (size, size), buf.getvalue()))
    return out


def build_stage():
    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)

    target = os.path.join(STAGE, INSTALL_DIR)
    shutil.copytree(SRC, target, symlinks=True)

    for junk in ("Config", "Profiles", "Logs", "Cache", "Backups", "Temp"):
        p = os.path.join(target, junk)
        if os.path.isdir(p):
            shutil.rmtree(p)

    with open(os.path.join(target, "PackageType"), "w", encoding="utf-8") as f:
        f.write("deb")

    appdir = os.path.join(STAGE, "usr/share/applications")
    os.makedirs(appdir, exist_ok=True)
    with open(os.path.join(appdir, "classbar.desktop"), "w", encoding="utf-8", newline="\n") as f:
        f.write(DESKTOP)

    for rel, data in make_icons():
        dest = os.path.join(STAGE, rel)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write(data)

    # 终端命令入口目录（实际软链在 tar 里生成，Windows 上建不了 symlink）
    os.makedirs(os.path.join(STAGE, "usr/bin"), exist_ok=True)

    print("stage ready:", STAGE)


def iter_stage():
    items = []
    for base, dirs, files in os.walk(STAGE):
        dirs.sort()
        files.sort()
        for d in dirs:
            full = os.path.join(base, d)
            items.append((os.path.relpath(full, STAGE).replace(os.sep, "/"), full, "dir"))
        for f in files:
            full = os.path.join(base, f)
            items.append((os.path.relpath(full, STAGE).replace(os.sep, "/"), full, "file"))
    items.sort(key=lambda x: x[0])
    return items


def tar_bytes(items):
    buf = io.BytesIO()
    md5s = []

    with tarfile.open(fileobj=buf, mode="w:gz", compresslevel=6, format=tarfile.GNU_FORMAT) as tar:
        for rel, full, kind in items:
            arc = "./" + rel
            if kind == "dir":
                tar.addfile(norm_tarinfo(tarfile.TarInfo(arc), 0o755, is_dir=True))
            elif os.path.islink(full):
                ti = tarfile.TarInfo(arc)
                ti.linkname = os.readlink(full)
                tar.addfile(norm_tarinfo(ti, 0o777, is_link=True))
            else:
                with open(full, "rb") as fh:
                    data = fh.read()
                ti = tarfile.TarInfo(arc)
                ti.size = len(data)
                tar.addfile(norm_tarinfo(ti, file_mode(rel)), io.BytesIO(data))
                md5s.append((hashlib.md5(data).hexdigest(), rel))

        # /usr/bin/classbar -> /opt/classbar/ClassIsland（终端命令入口）
        link = tarfile.TarInfo("./usr/bin/classbar")
        link.linkname = "/opt/classbar/ClassIsland"
        tar.addfile(norm_tarinfo(link, 0o777, is_link=True))
    return buf.getvalue(), md5s


def control_bytes(md5s, installed_kb):
    md5_text = "\n".join("%s  %s" % (h, p) for h, p in sorted(md5s, key=lambda x: x[1])) + "\n"
    control = (
        "Package: %s\n"
        "Version: %s\n"
        "Section: utils\n"
        "Priority: optional\n"
        "Architecture: %s\n"
        "Installed-Size: %d\n"
        "Maintainer: li2012China <li2012china@users.noreply.github.com>\n"
        "Homepage: https://classisland.tech\n"
        "Description: ClassBar - \u5c4f\u5e55\u9876\u90e8\u8bfe\u8868\u663e\u793a\u5de5\u5177\n"
        " ClassBar \u662f\u57fa\u4e8e ClassIsland \u7684\u7cbe\u7b80\u7248\u672c\uff0c\u5728\u5c4f\u5e55\u9876\u90e8\u5e38\u9a7b\u663e\u793a\u8bfe\u8868\u3001\n"
        " \u65f6\u95f4\u4e0e\u5012\u8ba1\u65f6\uff0c\u652f\u6301\u5165\u573a/\u6536\u8d77\u52a8\u753b\u3001\u8bfe\u95f4\u5185\u5bb9\u8f6e\u6362\u4e0e\u52a8\u753b\u6d4b\u8bd5\u3002\n"
        "Depends: %s\n"
    ) % (PKG, VERSION, ARCH, installed_kb, DEPENDS)

    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz", compresslevel=9, format=tarfile.GNU_FORMAT) as tar:
        for name, data, mode in (
            ("./control", control.encode("utf-8"), 0o644),
            ("./md5sums", md5_text.encode("utf-8"), 0o644),
            ("./postinst", POSTINST.encode("utf-8"), 0o755),
            ("./postrm", POSTRM.encode("utf-8"), 0o755),
        ):
            ti = tarfile.TarInfo(name)
            ti.size = len(data)
            tar.addfile(norm_tarinfo(ti, mode), io.BytesIO(data))
    return buf.getvalue()


def ar_member(name, data):
    header = "{:<16}{:<12}{:<6}{:<6}{:<8}{:<10}`\n".format(
        name + "/", EPOCH, 0, 0, "100644", len(data)
    ).encode("ascii")
    body = data + (b"\n" if len(data) % 2 else b"")
    return header, body


def build_deb():
    items = iter_stage()
    data_tar, md5s = tar_bytes(items)
    installed_kb = max(1, sum(os.path.getsize(f) for _, f, k in items if k == "file") // 1024)
    ctrl_tar = control_bytes(md5s, installed_kb)

    with open(OUT, "wb") as f:
        f.write(b"!<arch>\n")
        for name, data in (
            ("debian-binary", b"2.0\n"),
            ("control.tar.gz", ctrl_tar),
            ("data.tar.gz", data_tar),
        ):
            h, b = ar_member(name, data)
            f.write(h)
            f.write(b)

    print("deb built:", OUT)
    print("size MB:", round(os.path.getsize(OUT) / 1048576, 1))
    print("entries:", len(items))


if __name__ == "__main__":
    build_stage()
    build_deb()
