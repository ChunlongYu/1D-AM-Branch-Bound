"""Python prototype of the incremental (Type I/II) single-machine oracle.
Validates: (a) optimum == exhaustive optimum; (b) dominance soundness.
Reuses the exact enumerator from the Step-2 validator."""
import itertools, math, random, sys

def b_area(B,P): return sum(P[j][0] for j in B)
def b_proc(B,P,S,V,U):
    if not B: return 0.0
    return S+V*sum(P[j][1] for j in B)+U*max(P[j][2] for j in B)

# exhaustive optimum over ALL ordered batchings of `parts` from time 0
def exact_opt(parts,P,A,S,V,U):
    parts=frozenset(parts); memo={}
    def rec(R,t):
        if not R: return 0.0
        key=(R,round(t,6))
        if key in memo: return memo[key]
        best=math.inf
        Rl=sorted(R)
        for r in range(1,len(Rl)+1):
            for c in itertools.combinations(Rl,r):
                if b_area(c,P)>A+1e-9: continue
                Cn=t+b_proc(c,P,S,V,U)
                td=sum(max(0.0,Cn-P[j][3]) for j in c)
                v=td+rec(R-frozenset(c),Cn)
                if v<best: best=v
        memo[key]=best; return best
    return rec(parts,0.0)

# ---- validated node bound (iron) from the note ----
def node_LB(closed, open_set, R, P, A, S, V, U):
    F=0.0; ctard=0.0
    for B in closed:
        F+=b_proc(B,P,S,V,U); ctard+=sum(max(0.0,F-P[j][3]) for j in B)
    if open_set:
        a_r=b_area(open_set,P); v_r=sum(P[j][1] for j in open_set); h_r=max(P[j][2] for j in open_set)
        c_r=F+S+V*v_r+U*h_r; rho=A-a_r; ofloor=sum(max(0.0,c_r-P[j][3]) for j in open_set); has=True
    else:
        a_r=v_r=h_r=0.0; c_r=F; rho=0.0; ofloor=0.0; has=False
    Rl=sorted(R)
    lbpar=0.0
    for j in Rl:
        aj,vj,hj,dj=P[j]
        lj=(c_r+V*vj) if aj<=rho+1e-9 else (c_r+S+V*vj+U*hj)
        lbpar+=max(0.0,lj-dj)
    lbpos=0.0; q=len(Rl)
    if q>0:
        areas=sorted(P[j][0] for j in Rl); vols=sorted(P[j][1] for j in Rl)
        hs=sorted(P[j][2] for j in Rl); ds=sorted(P[j][3] for j in Rl)
        pa=pv=0.0
        for k in range(1,q+1):
            pa+=areas[k-1]; pv+=vols[k-1]
            bk=max(1,math.ceil((a_r+pa)/A-1e-9)); bk=min(bk,k+(1 if a_r>0 else 0))
            tok=sorted(([h_r] if has else [])+hs[0:k]); m=len(tok); bkc=min(bk,m)
            Hk=tok[-1]+sum(tok[0:bkc-1])
            Ck=F+bk*S+V*(v_r+pv)+U*Hk
            lbpos+=max(0.0,Ck-ds[k-1])
    return ctard+ofloor+max(lbpar,lbpos)

def full_obj(closed, open_set, P,S,V,U):  # leaf objective (all closed)
    F=0.0; tt=0.0
    for B in list(closed)+([open_set] if open_set else []):
        F+=b_proc(B,P,S,V,U); tt+=sum(max(0.0,F-P[j][3]) for j in B)
    return tt

def cr_tt(closed, open_set, P,S,V,U):
    F=0.0; tt=0.0
    for B in closed:
        F+=b_proc(B,P,S,V,U); tt+=sum(max(0.0,F-P[j][3]) for j in B)
    if open_set:
        c_r=F+b_proc(open_set,P,S,V,U); tt+=sum(max(0.0,c_r-P[j][3]) for j in open_set)
    else:
        c_r=F
    return c_r,tt

def inc_oracle(P,A,S,V,U,dom_mode):
    n=len(P); allmask=(1<<n)-1
    UB=full_obj([],frozenset(),P,S,V,U)  # =0 dummy; better: greedy
    # greedy EDD initial UB
    order=sorted(range(n),key=lambda j:P[j][3]); cl=[]; cur=[]; ar=0.0
    for j in order:
        if cur and ar+P[j][0]>A+1e-9: cl.append(frozenset(cur)); cur=[]; ar=0.0
        cur.append(j); ar+=P[j][0]
    if cur: cl.append(frozenset(cur))
    UB=full_obj(cl,frozenset(),P,S,V,U)
    dom={}
    def mask(s): 
        m=0
        for j in s: m|=(1<<j)
        return m
    def dom_key(closed,open_set):
        su=0
        for B in closed:
            for j in B: su|=(1<<j)
        for j in open_set: su|=(1<<j)
        if dom_mode=='su': return su
        if dom_mode=='su_open': return (su, mask(open_set))
        return None
    def register(closed,open_set):
        if dom_mode=='none': return True
        k=dom_key(closed,open_set); c_r,tt=cr_tt(closed,open_set,P,S,V,U)
        lab=(c_r,tt); vec=dom.setdefault(k,[])
        for (c,t) in vec:
            if c<=c_r+1e-9 and t<=tt+1e-9: return False
        dom[k]=[x for x in vec if not (c_r<=x[0]+1e-9 and tt<=x[1]+1e-9)]+[lab]
        return True
    nodes=0
    stack=[([],frozenset())]  # (closed list, open set)
    while stack:
        closed,open_set=stack.pop(); nodes+=1
        scheduled=set()
        for B in closed: scheduled|=set(B)
        scheduled|=set(open_set)
        R=[j for j in range(n) if j not in scheduled]
        if not R:
            obj=full_obj(closed,open_set,P,S,V,U)
            if obj<UB-1e-9: UB=obj
            continue
        lb=node_LB(closed,open_set,R,P,A,S,V,U)
        if lb>=UB-1e-9: continue
        # children
        kids=[]
        if not open_set:
            for j in R: kids.append((closed, frozenset([j])))
        else:
            maxidx=max(open_set); a_open=b_area(open_set,P)
            for j in R:  # Type I
                kids.append((closed+[open_set], frozenset([j])))
            for j in R:  # Type II
                if j>maxidx and a_open+P[j][0]<=A+1e-9:
                    kids.append((closed, open_set|{j}))
        for (ncl,nop) in kids:
            if node_LB(ncl,nop,[j for j in range(n) if j not in (set().union(*[set(b) for b in ncl],set(nop)) if ncl or nop else set())],P,A,S,V,U)>=UB-1e-9:
                pass
            if register(ncl,nop):
                stack.append((ncl,nop))
    return UB,nodes

def gen(n,rng):
    P=[]
    for _ in range(n):
        P.append((rng.randint(1,6), round(rng.uniform(0.5,5),3), round(rng.uniform(0.5,5),3), round(rng.uniform(0,25),3)))
    A=max(rng.randint(5,12),max(p[0] for p in P))
    return P,A,round(rng.uniform(1,5),3),round(rng.uniform(0.1,1),3),round(rng.uniform(0.1,1),3)

def main():
    rng=random.Random(int(sys.argv[2]) if len(sys.argv)>2 else 1)
    T=int(sys.argv[1]) if len(sys.argv)>1 else 800
    bad={'none':0,'su_open':0,'su':0}; tot=0
    ncount={'none':0,'su_open':0,'su':0}
    for _ in range(T):
        n=rng.randint(3,7); P,A,S,V,U=gen(n,rng)
        opt=exact_opt(list(range(n)),P,A,S,V,U); tot+=1
        for mode in ['none','su_open','su']:
            val,nodes=inc_oracle(P,A,S,V,U,mode); ncount[mode]+=nodes
            if abs(val-opt)>1e-6:
                bad[mode]+=1
                if bad[mode]<=2:
                    print(f"  [{mode}] MISMATCH val={val:.4f} exact={opt:.4f}  P={P} A={A} S={S} V={V} U={U}")
    print(f"instances={tot}")
    for mode in ['none','su_open','su']:
        print(f"  dom={mode:8s}  optimum-mismatches={bad[mode]:3d}  total_nodes={ncount[mode]}")

main()

# ---------- submask oracle (existing scheme) for head-to-head node count ----------
def submask_oracle(P,A,S,V,U):
    n=len(P)
    # greedy EDD UB
    order=sorted(range(n),key=lambda j:P[j][3]); cl=[]; cur=[]; ar=0.0
    for j in order:
        if cur and ar+P[j][0]>A+1e-9: cl.append(frozenset(cur)); cur=[]; ar=0.0
        cur.append(j); ar+=P[j][0]
    if cur: cl.append(frozenset(cur))
    UB=full_obj(cl,frozenset(),P,S,V,U)
    # manuscript bound: closed batches exact + max(par,pos) anchored at time_cursor
    def smask(batches):
        m=0
        for B in batches:
            for j in B: m|=(1<<j)
        return m
    def bound(batches):
        # all batches closed; anchor unassigned at time_cursor
        F=0.0; tt=0.0
        for B in batches:
            F+=b_proc(B,P,S,V,U); tt+=sum(max(0.0,F-P[j][3]) for j in B)
        sched=set()
        for B in batches: sched|=set(B)
        R=[j for j in range(n) if j not in sched]
        lbpar=sum(max(0.0,F+S+V*P[j][1]+U*P[j][2]-P[j][3]) for j in R)
        lbpos=0.0; q=len(R)
        if q>0:
            areas=sorted(P[j][0] for j in R); vols=sorted(P[j][1] for j in R)
            hs=sorted(P[j][2] for j in R); ds=sorted(P[j][3] for j in R); pa=pv=0.0
            for k in range(1,q+1):
                pa+=areas[k-1]; pv+=vols[k-1]
                bk=max(1,math.ceil(pa/A-1e-9)); bk=min(bk,k)
                Hk=sum(hs[0:bk-1])+hs[k-1]
                Ck=F+bk*S+V*pv+U*Hk
                lbpos+=max(0.0,Ck-ds[k-1])
        return tt+max(lbpar,lbpos),F,tt,R
    dom={}
    def reg(batches,F,tt):
        k=smask(batches); vec=dom.setdefault(k,[])
        for (c,t) in vec:
            if c<=F+1e-9 and t<=tt+1e-9: return False
        dom[k]=[x for x in vec if not (F<=x[0]+1e-9 and tt<=x[1]+1e-9)]+[(F,tt)]
        return True
    nodes=0; stack=[[]]
    while stack:
        batches=stack.pop(); nodes+=1
        lb,F,tt,R=bound(batches)
        if not R:
            if tt<UB-1e-9: UB=tt
            continue
        if lb>=UB-1e-9: continue
        # children: each nonempty area-feasible subset of R as next batch
        for r in range(1,len(R)+1):
            for c in itertools.combinations(sorted(R),r):
                if b_area(c,P)>A+1e-9: continue
                nb=batches+[frozenset(c)]
                _,F2,tt2,_=bound(nb)
                if reg(nb,F2,tt2): stack.append(nb)
    return UB,nodes

def headtohead():
    rng=random.Random(7); T=400
    ninc=0; nsub=0; mm=0
    for _ in range(T):
        n=rng.randint(3,7); P,A,S,V,U=gen(n,rng)
        vi,ni=inc_oracle(P,A,S,V,U,'su_open')
        vs,ns=submask_oracle(P,A,S,V,U)
        if abs(vi-vs)>1e-6: mm+=1
        ninc+=ni; nsub+=ns
    print(f"head-to-head over {T} instances (n=3..7):")
    print(f"  incremental(su_open) total_nodes={ninc}")
    print(f"  submask(existing)    total_nodes={nsub}")
    print(f"  optimum mismatches between the two: {mm}")

if __name__=='__main__' and len(sys.argv)>1 and sys.argv[1]=='h2h':
    headtohead()
