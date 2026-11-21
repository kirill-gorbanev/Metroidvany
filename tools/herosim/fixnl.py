"""Чинит строки C#, в которые при вставке через heredoc попали настоящие переводы строк вместо \\n.
Использование: python fixnl.py <file.cs> <маркер начала> <маркер конца>
Внутри куска [начало, конец) каждый перевод строки внутри строкового литерала заменяется на \\n."""
import sys

p, a_mark, b_mark = sys.argv[1], sys.argv[2], sys.argv[3]
raw = open(p, 'rb').read().decode('utf-8')
a = raw.index(a_mark)
b = raw.index(b_mark, a)
blk = raw[a:b]
out = []
i = 0
inq = None
BS = chr(92)
while i < len(blk):
    ch = blk[i]
    if inq is None:
        if ch in '"\'':
            inq = ch
        elif blk.startswith('//', i):
            j = blk.index('\n', i)
            out.append(blk[i:j])
            i = j
            continue
        out.append(ch)
    else:
        if ch == BS:
            out.append(ch)
            out.append(blk[i + 1])
            i += 2
            continue
        if ch == '\r':
            i += 1
            continue
        if ch == '\n':
            out.append(BS + 'n')
            i += 1
            continue
        if ch == inq:
            inq = None
        out.append(ch)
    i += 1
open(p, 'wb').write((raw[:a] + ''.join(out) + raw[b:]).encode('utf-8'))
print('ok')
