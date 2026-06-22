import itertools, math, random
import inc_oracle_proto as M

def inc_work(P,A,S,V,U):  # incremental su_open: count nodes + children(bound evals)
    n=len(P)
    order=sorted(range(n),key=lambda j:P[j][3]); cl=[]; cur=[]; ar=0.0
    for j in order:
        if cur and ar+P[j][0]>A+1e-9: cl.append(frozenset(cur)); cur=[]; ar=0.0
        cur.append(j); ar+=P[j][0]
    if cur: cl.append(frozenset(cur))
    UB=M.full_obj(cl,frozenset(),P,S,V,U)
    dom={}
    def cr_tt(closed,op): return M.cr_tt(closed,op,P,S,V,U)
    def key(closed,op):
        su=0
        for B in closed:
            for j in B: su|=1<<j
        for j in op: su|=1<<j
        om=0
        for j in op: om|=1<<j
        return (su,om)
    def reg(closed,op):
        k=key(closed,op); c_r,tt=cr_tt(closed,op); vec=dom.setdefault(k,[])
        for c,t in vec:
            if c<=c_r+1e-9 and t<=tt+1e-9: return False
        dom[k]=[x for x in vec if not (c_r<=x[0]+1e-9 and tt<=x[1]+1e-9)]+[(c_r,tt)]
        return True
    nodes=0; evals=0; stack=[([],frozenset())]
    while stack:
        closed,op=stack.pop(); nodes+=1
        sched=set()
        for B in closed: sched|=set(B)
        sched|=set(op)
        R=[j for j in range(n) if j not in sched]
        if not R:
            obj=M.full_obj(closed,op,P,S,V,U)
            if obj<UB-1e-9: UB=obj
            continue
        lb=M.node_LB(closed,op,R,P,A,S,V,U); 
        if lb>=UB-1e-9: continue
        kids=[]
        if not op:
            for j in R: kids.append((closed,frozenset([j])))
        else:
            mi=max(op); ao=M.b_area(op,P)
            for j in R: kids.append((closed+[op],frozenset([j])))
            for j in R:
                if j>mi and ao+P[j][0]<=A+1e-9: kids.append((closed,op|{j}))
        for ncl,nop in kids:
            evals+=1
            nsched=set()
            for B in ncl: nsched|=set(B)
            nsched|=set(nop)
            nR=[j for j in range(n) if j not in nsched]
            if M.node_LB(ncl,nop,nR,P,A,S,V,U)>=UB-1e-9: continue
            if reg(ncl,nop): stack.append((ncl,nop))
    return UB,nodes,evals

def sub_work(P,A,S,V,U):
    n=len(P)
    order=sorted(range(n),key=lambda j:P[j][3]); cl=[]; cur=[]; ar=0.0
    for j in order:
        if cur and ar+P[j][0]>A+1e-9: cl.append(frozenset(cur)); cur=[]; ar=0.0
        cur.append(j); ar+=P[j][0]
    if cur: cl.append(frozenset(cur))
    UB=M.full_obj(cl,frozenset(),P,S,V,U)
    def smask(b):
        m=0
        for B in b:
            for j in B: m|=1<<j
        return m
    def bound(b):
        F=0.0; tt=0.0
        for B in b:
            F+=M.b_proc(B,P,S,V,U); tt+=sum(max(0.0,F-P[j][3]) for j in B)
        sched=set()
        for B in b: sched|=set(B)
        R=[j for j in range(n) if j not in sched]
        lbpar=sum(max(0.0,F+S+V*P[j][1]+U*P[j][2]-P[j][3]) for j in R)
        lbpos=0.0; q=len(R)
        if q>0:
            ar2=sorted(P[j][0] for j in R); vo=sorted(P[j][1] for j in R)
            hs=sorted(P[j][2] for j in R); ds=sorted(P[j][3] for j in R); pa=pv=0.0
            for k in range(1,q+1):
                pa+=ar2[k-1]; pv+=vo[k-1]; bk=min(max(1,math.ceil(pa/A-1e-9)),k)
                Ck=F+bk*S+V*pv+U*(sum(hs[0:bk-1])+hs[k-1]); lbpos+=max(0.0,Ck-ds[k-1])
        return tt+max(lbpar,lbpos),F,tt,R
    dom={}
    def reg(b,F,tt):
        k=smask(b); vec=dom.setdefault(k,[])
        for c,t in vec:
            if c<=F+1e-9 and t<=tt+1e-9: return False
        dom[k]=[x for x in vec if not (F<=x[0]+1e-9 and tt<=x[1]+1e-9)]+[(F,tt)]
        return True
    nodes=0; evals=0; stack=[[]]
    while stack:
        b=stack.pop(); nodes+=1
        lb,F,tt,R=bound(b)
        if not R:
            if tt<UB-1e-9: UB=tt
            continue
        if lb>=UB-1e-9: continue
        for r in range(1,len(R)+1):
            for c in itertools.combinations(sorted(R),r):
                if M.b_area(c,P)>A+1e-9: continue
                evals+=1
                nb=b+[frozenset(c)]
                _,F2,tt2,_=bound(nb)
                if reg(nb,F2,tt2): stack.append(nb)
    return UB,nodes,evals

rng=random.Random(7); T=400
ni=ns=ei=es=mm=0
for _ in range(T):
    n=rng.randint(3,7); P,A,S,V,U=M.gen(n,rng)
    vi,n_i,e_i=inc_work(P,A,S,V,U); vs,n_s,e_s=sub_work(P,A,S,V,U)
    if abs(vi-vs)>1e-6: mm+=1
    ni+=n_i; ns+=n_s; ei+=e_i; es+=e_s
print(f"over {T} instances (n=3..7):")
print(f"  incremental(su_open): nodes={ni:7d}  bound_evals(children)={ei:8d}")
print(f"  submask(existing)   : nodes={ns:7d}  bound_evals(children)={es:8d}")
print(f"  mismatches={mm}")
