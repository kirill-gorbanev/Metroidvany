"""Эталон + отчёт части зон: секции «===== АРЕНА N» из отчёта заменяют такие же в эталоне (живые строки «~» — из отчёта)."""
import sys


def sections(p):
    out, order, cur = {}, [], None
    for l in open(p, encoding='utf-8'):
        if l.startswith('===== АРЕНА'):
            cur = l.strip(); order.append(cur); out[cur] = []
        elif cur:
            out[cur].append(l.rstrip('\n'))
    return out, order


base, order = sections(sys.argv[1])
new, _ = sections(sys.argv[2])
base.update(new)
for k in order + [k for k in new if k not in order]:
    print(k)
    for l in base[k]:
        print(l)
