"""TeamWorkflow 문서 검사·생성 도구.

    python Tools/check.py           검사만 한다. 문제가 있거나 생성물이 낡았으면 종료 코드 1
    python Tools/check.py --write   기능 표(Design/README.md)와 현황(Meetings/현황.md)을 다시 만든다

검사: 깨진 링크, 결정 로그에 없는 D-번호, 반영 위치가 없는 유효 결정, 기획서 머리(📌) 칸 누락.
생성: 기능 표 = 각 기획서 머리 정보. 현황 = 반영 대기(담당별), 담당 미정, 열린 요청(받는 사람별), 기능 문서 낡음, 확인 필요(회의 안건 후보).
반영 대기 = 결정 로그의 `반영 기획서`가 가리키는 문서의 `변경 이력`에 그 D-번호가 아직 없는 것.
표준 라이브러리만 쓴다. Templates/ 안의 예시 링크는 검사하지 않는다.
"""
import re
import sys
import urllib.parse
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
ROOT = Path(__file__).resolve().parents[1]
DESIGN, LOG, STATUS = ROOT / "Design", ROOT / "Meetings" / "README.md", ROOT / "Meetings" / "현황.md"
SKIP_DIRS = {"_보관", "_원문", "_제외", "images"}
CATEGORY_ORDER = ["프로젝트", "세계관", "월드", "전투", "성장", "적", "UI"]
STATES = {"초안", "확정", "구현 중", "완료", "보류"}
FIELDS = ["기획자", "구현 담당", "상태", "관련 결정"]
DEFAULT_OWNER = "NMJ"  # 구현 담당이 미정이면 NMJ가 기획서를 고친다
LEFT_MEMBERS = {"CHG"}  # 팀에서 빠진 사람. 이 사람에게 간 요청은 다시 지정해야 한다
TEAM_LEAD = "NGH"  # D-059. '팀장'에게 간 요청은 이 사람 몫으로 모은다
TABLE_START, TABLE_END = "<!-- 기능 표 시작: Tools/check.py --write 가 생성. 직접 고치지 않음 -->", "<!-- 기능 표 끝 -->"
D_ID = re.compile(r"D-\d{3}")


def read(p):
    return p.read_text(encoding="utf-8")


def links(text):
    """'](대상)'을 괄호 짝을 맞춰 찾는다. 파일명에 괄호가 들어가도 깨지지 않게 한다."""
    i = 0
    while (i := text.find("](", i)) >= 0:
        j, depth = i + 2, 1
        while j < len(text) and depth:
            depth += {"(": 1, ")": -1}.get(text[j], 0)
            j += 1
        yield text[i + 2:j - 1]
        i = j


def section(text, title):
    """'## n. 제목' 섹션 본문. 없으면 None."""
    m = re.search(rf"^##\s*(?:\d+\.\s*)?{title}\s*$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return m.group(1) if m else None


def applied_ids(path):
    """반영된 결정 = 변경 이력에 적힌 D-번호. 변경 이력이 없는 문서(Rules 등)는 본문 전체.
    머리의 '관련 결정'은 세지 않는다. 충돌을 메모하려고 적어 둔 번호까지 반영으로 오인하기 때문이다."""
    text = read(path)
    return set(D_ID.findall(section(text, "변경 이력") or text))


def relink(line, src_dir, base):
    """다른 위치로 옮겨 적는 줄의 상대 링크를 base 기준으로 고친다."""
    def fix(t):
        if t.startswith(("http", "mailto", "#")):
            return t
        target = (src_dir / urllib.parse.unquote(t.split("#")[0])).resolve()
        return rel(target, base) if ROOT in target.parents else t
    out, i = [], 0
    for t in links(line):
        j = line.index("](" + t, i) + 2
        out.append(line[i:j] + fix(t))
        i = j + len(t)
    return "".join(out) + line[i:]


def design_docs():
    for p in sorted(DESIGN.rglob("*.md")):
        if p.name != "README.md" and not SKIP_DIRS & set(p.relative_to(DESIGN).parts):
            yield p


def meta(p):
    text = read(p)
    fields = dict(re.findall(r"\*\*(기획자|구현 담당|상태|관련 결정):\*\*[ \t]*([^\n]*)", text))
    title = re.search(r"^#\s+(.+)$", text, re.M)
    parts = p.relative_to(DESIGN).parts
    return {**fields, "title": title.group(1).strip() if title else p.stem,
            "category": parts[0] if len(parts) > 1 else "프로젝트", "path": p}


def owner(p):
    """기획서를 고칠 사람: 구현 담당의 첫 이니셜, 없으면 NMJ. Rules 등 기획서가 아니면 YPH(회의 반영)."""
    if DESIGN not in p.parents:
        return "YPH"
    m = re.search(r"\b[A-Z]{3}\b", meta(p).get("구현 담당", ""))
    return m.group(0) if m else DEFAULT_OWNER


def parse_log():
    rows = []
    for line in read(LOG).splitlines():
        if not re.match(r"^\| D-\d{3} \|", line):
            continue
        cells = [c.strip() for c in line.strip("|").split(" | ")]
        targets = [(LOG.parent / urllib.parse.unquote(t)).resolve() for t in links(cells[4])]
        rows.append({"id": cells[0], "decision": cells[2], "target_cell": cells[4], "targets": targets,
                     "status": cells[5]})
    return rows


def rel(p, base):
    return Path(*[".."] * len(base.relative_to(ROOT).parts), p.relative_to(ROOT)).as_posix()


def check(rows, docs):
    errors = []
    for p in ROOT.rglob("*.md"):
        if "Templates" in p.relative_to(ROOT).parts or p == STATUS:  # 현황은 생성물이라 relink로 경로를 맞춘다
            continue
        for t in links(read(p)):
            t = t.split("#")[0].strip()
            if t and not t.startswith(("http", "mailto")) and not (p.parent / urllib.parse.unquote(t)).exists():
                errors.append(f"깨진 링크: {p.relative_to(ROOT).as_posix()} → {t}")
    logged = {r["id"] for r in rows}
    for p in list(docs) + list((ROOT / "Rules").glob("*.md")):
        for d in sorted(set(D_ID.findall(read(p))) - logged):
            errors.append(f"결정 로그에 없는 번호: {d} ({p.relative_to(ROOT).as_posix()})")
    for r in rows:
        if r["status"].startswith(("유효", "일부")) and not r["targets"]:
            errors.append(f"반영 위치 없음: {r['id']} ({r['target_cell']}) — 프로젝트 문서·기획서·Rules 중 하나를 지정")
    for p in docs:
        m = meta(p)
        missing = [f for f in FIELDS if not m.get(f)]
        if missing:
            errors.append(f"머리 정보 누락: {p.relative_to(ROOT).as_posix()} ({', '.join(missing)})")
        elif m["상태"] not in STATES:
            errors.append(f"잘못된 상태: {p.relative_to(ROOT).as_posix()} ({m['상태']})")
    for q in request_docs():  # 요청서는 상태와 폴더(진행/완료)가 맞아야 한다
        where = q["path"].relative_to(ROOT).as_posix()
        done = q["state"].startswith(("완료", "반려"))
        if q["folder"] not in ("진행", "완료"):
            errors.append(f"요청서 위치: {where} — Requests/진행/ 또는 Requests/완료/에 둘 것")
        elif done != (q["folder"] == "완료"):
            errors.append(f"요청서 상태와 폴더가 다름: {where} (상태: {q['state']})")
    return errors


def request_docs():
    """Requests/ 아래 요청서(README 제외)의 머리 표 값."""
    for p in sorted((ROOT / "Requests").rglob("*.md")):
        if p.name == "README.md":
            continue
        text = read(p)
        cell = lambda name: (re.search(rf"^\|\s*{name}\s*\|\s*([^|\n]*)\|", text, re.M) or [None, "?"])[1].strip()
        parts = p.relative_to(ROOT / "Requests").parts
        yield {"path": p, "folder": parts[0] if len(parts) > 1 else "", "state": cell("상태"),
               "to": cell("받는 사람"), "date": cell("요청일"),
               "title": (re.search(r"^#\s+요청:\s*(.+)$", text, re.M) or [None, p.stem])[1]}


def feature_table(docs):
    ms = sorted((meta(p) for p in docs), key=lambda m: (
        CATEGORY_ORDER.index(m["category"]) if m["category"] in CATEGORY_ORDER else 99, m["path"].name))
    out = ["| 분류 | 기능 | 기획자 | 구현 담당 | 상태 | 마지막 반영 결정 |", "|---|---|---|---|---|---|"]
    for m in ms:
        last = max(applied_ids(m["path"]), default="-")
        link = m["path"].relative_to(DESIGN).as_posix()
        out.append(f"| {m['category']} | [{m['title']}]({link}) | {m.get('기획자', '?')} | "
                   f"{m.get('구현 담당', '?')} | {m.get('상태', '?')} | {last} |")
    return "\n".join(out)


def status_page(rows, docs, errors):
    base = STATUS.parent
    pending = {}
    for r in rows:
        if not r["status"].startswith(("유효", "일부")):
            continue
        for t in r["targets"]:
            if t.exists() and r["id"] not in applied_ids(t):
                pending.setdefault(owner(t), []).append(
                    f"| {r['id']} | {r['decision']} | [{t.stem}]({rel(t, base)}) |")
    out = ["# 현황", "", "> `Tools/check.py --write`가 만든 파일입니다. 직접 고치지 않습니다. 회의 전과 기획서 수정 후에 다시 만듭니다.", ""]
    out += ["## 반영 대기", "", "결정 로그에는 있지만 해당 문서의 `변경 이력`에 아직 없는 결정입니다. 담당자가 반영하면 사라집니다.", ""]
    if not pending:
        out += ["없음", ""]
    for who in sorted(pending):
        out += [f"### {who} ({len(pending[who])}건)", "", "| 번호 | 결정 | 문서 |", "|---|---|---|", *pending[who], ""]
    undecided = [meta(p) for p in docs if re.search(r"미정", meta(p).get("구현 담당", ""))]
    out += ["## 구현 담당 미정", ""]
    out += [f"- [{m['title']}]({rel(m['path'], base)}) — 문서는 {DEFAULT_OWNER}가 고침" for m in undecided] or ["없음"]
    out += ["", "## 열린 요청", "", "`Requests/진행/`의 요청서입니다. 받는 사람이 처리해 상태를 `완료`·`반려`로 고치고 `Requests/완료/`로 옮기면 사라집니다.", ""]
    requests = {}
    for q in request_docs():
        if q["folder"] != "진행":
            continue
        who = (re.search(r"\b[A-Z]{3}\b", q["to"]) or [TEAM_LEAD if "팀장" in q["to"] else q["to"]])[0]
        note = " ⚠ 받는 사람이 팀에 없음. 다시 지정" if who in LEFT_MEMBERS else ""
        requests.setdefault(who, []).append(
            f"| [{q['title']}]({rel(q['path'], base)}) | {q['date']} | {q['state']}{note} |")
    if not requests:
        out += ["없음"]
    for who in sorted(requests):
        out += [f"### {who} ({len(requests[who])}건)", "", "| 요청 | 요청일 | 상태 |", "|---|---|---|", *requests[who], ""]
    out += ["", "## 기능 문서 낡음", "",
            "기능 문서의 `관련 기획서` 기준 결정보다 기획서에 더 새 결정이 반영된 경우입니다. 구현과 기능 문서를 기획에 맞춘 뒤 기준 결정을 올리면 사라집니다.", ""]
    stale, unmarked = [], []
    for p in sorted((ROOT / "Features").glob("*.md")):
        if p.name == "README.md":
            continue
        line = next((l for l in read(p).splitlines() if "관련 기획서" in l), None)
        if line is None:
            unmarked.append(f"- [{p.stem}]({rel(p, base)})")
            continue
        baseline = max(D_ID.findall(line), default=None)
        for t in links(line):
            design = (p.parent / urllib.parse.unquote(t)).resolve()
            if DESIGN not in design.parents or not design.exists():
                continue
            latest = max(applied_ids(design), default=None)
            if latest and (baseline is None or latest > baseline):
                stale.append(f"| [{p.stem}]({rel(p, base)}) | [{meta(design)['title']}]({rel(design, base)}) "
                             f"| {baseline or '없음'} | {latest} |")
    out += ["| 기능 문서 | 기획서 | 기준 결정 | 기획서 최신 반영 |", "|---|---|---|---|", *stale] if stale else ["없음"]
    if unmarked:
        out += ["", "`관련 기획서` 표시가 없어 비교하지 못한 문서:", "", *unmarked]
    out += ["", "## 확인 필요 (회의 안건 후보)", ""]
    for p in docs:
        body = section(read(p), "확인 필요")
        items = [l for l in (body or "").splitlines() if l.startswith("- ") and l.strip() != "- 없음"]
        if items:
            out += [f"### [{meta(p)['title']}]({rel(p, base)})", "", *[relink(l, p.parent, base) for l in items], ""]
    if errors:
        out += ["## 검사 문제", "", *[f"- {e}" for e in errors], ""]
    return "\n".join(out).rstrip() + "\n"


def main():
    write = "--write" in sys.argv
    rows, docs = parse_log(), list(design_docs())
    errors = check(rows, docs)
    readme = read(DESIGN / "README.md")
    if TABLE_START not in readme or TABLE_END not in readme:
        errors.append("Design/README.md에 기능 표 표시(<!-- 기능 표 시작/끝 -->)가 없음")
        new_readme = readme
    else:
        a, b = readme.index(TABLE_START) + len(TABLE_START), readme.index(TABLE_END)
        new_readme = readme[:a] + "\n" + feature_table(docs) + "\n" + readme[b:]
    new_status = status_page(rows, docs, errors)
    stale = [p.relative_to(ROOT).as_posix() for p, new in ((DESIGN / "README.md", new_readme), (STATUS, new_status))
             if not p.exists() or read(p) != new]
    if write:
        (DESIGN / "README.md").write_text(new_readme, encoding="utf-8", newline="")
        STATUS.write_text(new_status, encoding="utf-8", newline="")
        print("생성:", ", ".join(stale) or "바뀐 것 없음")
    elif stale:
        errors.append(f"생성물이 최신이 아님: {', '.join(stale)} — `python Tools/check.py --write` 실행")
    pending = new_status.split("## 구현 담당 미정")[0].count("\n| D-")
    print(f"기획서 {len(docs)}개, 결정 {len(rows)}개, 반영 대기 {pending}건, 문제 {len(errors)}건")
    for e in errors:
        print(" -", e)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
