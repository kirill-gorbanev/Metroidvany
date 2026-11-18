import re, sys
rows=open("tr_all.txt",encoding="utf-8").read().split("\n")
f=lambda s: float(s.replace(",","."))
sel=[]
for r in rows:
    nums=re.findall(r"-?\d+,\d\d",r)
    stm=re.search(r" ([A-Za-z]+) ",r)
    if len(nums)<13 or not stm: continue
    v=[f(x) for x in nums]
    sel.append((v[0],stm.group(1),v[1],v[2],(v[5],v[6]),(v[7],v[8]),(v[9],v[10]),(v[11],v[12]),v[3],v[4]))
print(len(sel), sel[0][0] if sel else None, sel[-1][0] if sel else None)
mode = sys.argv[1] if len(sys.argv)>1 else "flat"
if mode=="flat":
    w=[s for s in sel if s[1]=="Active" and s[3]<0.1]
    best=None
    for i in range(len(w)-140):
        if abs(w[i+140][2]-w[i][2])>1.5 and abs(w[i+140][0]-w[i][0]-1.4)<0.05: best=i;break
    print("best",best)
    rng = w[best:best+140:4] if best is not None else []
else:
    a,b=float(sys.argv[2]),float(sys.argv[3]); step=int(sys.argv[4]) if len(sys.argv)>4 else 3
    rng=[s for s in sel if a<=s[0]<=b][::step]
for s in rng:
    t,st,rx,ry,Lp,Lg,Rp,Rg,pp,pg=s
    print(f"{t:6.2f} {st[:4]} rx={rx:5.2f},{ry:4.2f} pel {pp:.2f}/{pg:.2f} L dx{Lp[0]-Lg[0]:+.2f} y{Lp[1]:.2f}/{Lg[1]:.2f}   R dx{Rp[0]-Rg[0]:+.2f} y{Rp[1]:.2f}/{Rg[1]:.2f}")
