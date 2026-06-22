// =============================================================================
//  Step 4: Lagrangian-decomposition Branch-and-Bound (parallel-machine AM).
//  Node bound = residual Lagrangian over the FREE parts:
//     g_m(u) = prizeOracle(cand=S_m∪free, mandatory=S_m, reward=u on free / 0 on base)
//     LB(N,u) = sum_{j in free} u_j + sum_m g_m(u)   (subgradient, warm-started)
//  A fully-assigned node has free=∅ -> LB = sum_m Phi(S_m) = exact objective,
//  so leaves and internal nodes are handled uniformly.
//  Branch: canonical machine-opening; best-first on LB.
// =============================================================================
#include "prize_oracle.h"
#include "InstanceData.h"
#include <bits/stdc++.h>
using namespace std;

struct Inst { int n,M; vector<double> l,w,h,v,d; double S,V,U,area; };
Inst I;

// Phi(set) via prize oracle (u=0, mandatory = whole set)
double Phi(const vector<int>& Q){
    if(Q.empty()) return 0.0;
    vector<double> z(I.n,0.0);
    auto r = prizeCollectSingleMachine(Q,I.l,I.w,I.h,I.v,I.d,I.S,I.V,I.U,I.area,z,Q);
    return r.value;
}

// ---- cheap analytical bounds (same family as the base B&B) ----
double singletonT(int j){ return max(0.0, I.S+I.V*I.v[j]+I.U*I.h[j]-I.d[j]); }
double lbParSet(const vector<int>& Q){ double s=0; for(int j:Q) s+=singletonT(j); return s; }
double lbPosSet(const vector<int>& Q){
    int q=Q.size(); if(!q) return 0;
    vector<double> a(q),hh(q),vv(q),dd(q);
    for(int k=0;k<q;++k){int j=Q[k];a[k]=I.l[j]*I.w[j];hh[k]=I.h[j];vv[k]=I.v[j];dd[k]=I.d[j];}
    sort(a.begin(),a.end());sort(hh.begin(),hh.end());sort(vv.begin(),vv.end());sort(dd.begin(),dd.end());
    vector<double> hp(q+1,0); for(int t=1;t<=q;++t) hp[t]=hp[t-1]+hh[t-1];
    double As=0,Vs=0,tot=0;
    for(int k=1;k<=q;++k){ As+=a[k-1];Vs+=vv[k-1];
        int b=max(1,(int)ceil(As/I.area-1e-9)); b=min(b,k);
        double Hk=hp[b-1]+hh[k-1], Ck=b*I.S+I.V*Vs+I.U*Hk;
        if(Ck-dd[k-1]>0) tot+=Ck-dd[k-1]; }
    return tot;
}
// node cheap bound = sum_m max(par,pos)(S_m) + sum_{free} singleton
double cheapNodeLB(const vector<vector<int>>& assign){
    double s=0; vector<char> asg(I.n,0);
    for(auto&A:assign){ for(int j:A) asg[j]=1; s+=max(lbParSet(A),lbPosSet(A)); }
    for(int j=0;j<I.n;++j) if(!asg[j]) s+=singletonT(j);
    return s;
}

struct LNode {
    vector<vector<int>> assign;   // assign[m]
    vector<double> u;             // duals (size n), warm-start
    double LB; int depth;
};
struct Cmp { bool operator()(const LNode&a,const LNode&b)const{ return a.LB>b.LB; } };

long long g_oracleCalls=0;

// residual Lagrangian node bound; refines node.u in place; returns best LB
double lagrBound(LNode& nd, double UB, int iters){
    int n=I.n, M=I.M;
    vector<char> assignedFlag(n,0);
    for(auto& a: nd.assign) for(int j:a) assignedFlag[j]=1;
    vector<int> freeParts; for(int j=0;j<n;++j) if(!assignedFlag[j]) freeParts.push_back(j);

    if(freeParts.empty()){           // full assignment -> exact objective
        double Z=0; for(int m=0;m<M;++m) Z+=Phi(nd.assign[m]);
        g_oracleCalls+=M;
        return Z;
    }
    vector<double>& u = nd.u;
    double bestLB=-1e18, rho=2.0; int stall=0, noImp=0;
    for(int it=0; it<iters; ++it){
        vector<double> rw(n,0.0); for(int j:freeParts) rw[j]=u[j];
        vector<int> coverage(n,0);
        double Lu=0; for(int j:freeParts) Lu+=u[j];
        // dedup machines sharing the same assigned set: g_m identical -> solve once, weight by count
        map<vector<int>,int> groups;          // sorted assigned set -> #machines
        for(int m=0;m<M;++m){ vector<int> key=nd.assign[m]; sort(key.begin(),key.end()); groups[key]++; }
        for(auto& kv : groups){
            const vector<int>& base = kv.first; int cnt = kv.second;
            vector<int> cand=base; cand.insert(cand.end(),freeParts.begin(),freeParts.end());
            auto r=prizeCollectSingleMachine(cand,I.l,I.w,I.h,I.v,I.d,I.S,I.V,I.U,I.area,rw,base);
            ++g_oracleCalls;
            Lu += cnt * r.value;
            for(int id:r.chosen) if(!assignedFlag[id]) coverage[id] += cnt;
        }
        if(Lu>bestLB+1e-9){bestLB=Lu;stall=0;noImp=0;}
        else { if(++stall>=8){rho*=0.5;stall=0;} if(++noImp>=12) break; }  // 收敛即停
        double nrm=0; vector<double> g(n,0.0);
        for(int j:freeParts){ g[j]=1.0-coverage[j]; nrm+=g[j]*g[j]; }
        if(nrm<1e-12) break;
        double step=rho*max(0.0,(UB-Lu))/nrm; if(step<1e-9) step=1e-3;
        for(int j:freeParts) u[j]+=step*g[j];
        if(bestLB>=UB-1e-9) break;   // already prunable
    }
    return bestLB;
}

// greedy initial incumbent: try several orderings, balanced assignment, keep best
double greedyUB(vector<vector<int>>& bestAssign){
    double best=1e18;
    for(int rule=0; rule<4; ++rule){
        vector<int> ord(I.n); iota(ord.begin(),ord.end(),0);
        sort(ord.begin(),ord.end(),[&](int a,int b){
            switch(rule){
                case 0: return I.d[a]<I.d[b];                       // EDD
                case 1: return I.l[a]*I.w[a]>I.l[b]*I.w[b];         // area desc
                case 2: return I.v[a]>I.v[b];                       // volume desc
                default:return I.h[a]>I.h[b];                       // height desc
            }});
        vector<vector<int>> A(I.M);
        for(int j:ord){ int r=0; for(int m=1;m<I.M;++m) if(A[m].size()<A[r].size()) r=m; A[r].push_back(j); }
        double Z=0; for(int m=0;m<I.M;++m) Z+=Phi(A[m]);
        if(Z<best){ best=Z; bestAssign=A; }
    }
    return best;
}

int candMachines(const LNode& nd){ int q=0; for(auto&a:nd.assign) if(!a.empty())++q; return min(q+1,I.M); }

int main(int argc,char**argv){
    string inst=argc>1?argv[1]:"12part"; int M=argc>2?atoi(argv[2]):2;
    int iters=argc>3?atoi(argv[3]):60; double tl=argc>4?atof(argv[4]):300;
    double gamma=argc>5?atof(argv[5]):0.6;   // 仅当 cheapLB >= gamma*UB 才调 Lagrangian
    int childIters=iters;  // 子节点也用满额迭代(界质量优先)
    long long cheapPruned=0, lagrCalls=0;
    MachineInfo m; vector<PartInfo> ps; PartLists pl;
    if(!readMachineAndParts("../data/"+inst+".txt",m,ps,pl)){printf("read fail\n");return 1;}
    I.n=ps.size(); I.M=M; I.l=pl.lengths;I.w=pl.widths;I.h=pl.heights;I.v=pl.volumes;I.d=pl.due_dates;
    I.S=m.setup_time;I.V=m.scanning_speed;I.U=m.recoater_speed;I.area=m.length*m.width;

    auto t0=chrono::steady_clock::now();
    vector<vector<int>> bestA; double UB=greedyUB(bestA);

    LNode root; root.assign.assign(M,{}); root.u.assign(I.n,0.0); root.depth=0;
    root.LB=lagrBound(root,UB,iters);   // root: full subgradient
    priority_queue<LNode,vector<LNode>,Cmp> pq; pq.push(root);
    long long nodes=0; bool timeout=false;

    while(!pq.empty()){
        if(chrono::duration<double>(chrono::steady_clock::now()-t0).count()>tl){timeout=true;break;}
        LNode cur=pq.top(); pq.pop(); ++nodes;
        if(cur.LB>=UB-1e-9) continue;
        // free parts
        vector<char> af(I.n,0); for(auto&a:cur.assign)for(int j:a)af[j]=1;
        vector<int> freeP; for(int j=0;j<I.n;++j) if(!af[j]) freeP.push_back(j);
        if(freeP.empty()){                          // leaf: evaluate exactly
            double Z=0; for(int mm=0;mm<I.M;++mm) Z+=Phi(cur.assign[mm]);
            if(Z<UB-1e-9){UB=Z;bestA=cur.assign;} continue;
        }
        // branch part = earliest due date among free
        int jb=freeP[0]; for(int j:freeP) if(I.d[j]<I.d[jb]) jb=j;
        int K=candMachines(cur);
        for(int r=0;r<K;++r){
            LNode ch=cur; ch.assign[r].push_back(jb); ch.depth=cur.depth+1;
            // node-level cheap-then-exact: cheap analytical bound first
            double cheap=cheapNodeLB(ch.assign);
            if(cheap>=UB-1e-9){ ++cheapPruned; continue; }   // pruned cheaply, no Lagrangian
            double lb=cheap;
            if(cheap>=gamma*UB){                              // hard node -> refine with Lagrangian
                double lag=lagrBound(ch,UB,childIters); ++lagrCalls;
                lb=max(cheap,lag);
                if(lb>=UB-1e-9) continue;
            }
            ch.LB=lb; pq.push(ch);
        }
    }
    double sec=chrono::duration<double>(chrono::steady_clock::now()-t0).count();
    printf("%-12s M=%d gamma=%.2f  obj=%.4f  %s  nodes=%lld  cheapPruned=%lld  lagrCalls=%lld  oracleCalls=%lld  time=%.2fs\n",
           inst.c_str(),M,gamma,UB, timeout?"TIMEOUT":"OPTIMAL", nodes, cheapPruned, lagrCalls, g_oracleCalls, sec);
    return 0;
}
