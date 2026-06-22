// =============================================================================
//  New single-machine 1D-AM batch-scheduling ORACLE (total tardiness)
//  - Type I / Type II branching (increasing-index symmetry break)
//  - Lower bound = closed(exact) + open-batch floor + max(LB_par, LB_pos):
//      LB_par : per-part parallel/merge-min completion floor
//      LB_pos : positional bound (area->#batches, volume, partition-min height),
//               per docs/Incremental_Branching_Bounds.md sec 4.1 ("iron"),
//               computed only on shallow nodes (|R| >= QFRAC*n) to amortize cost
//  - Dominance class 1/2: key (scheduled set, open-batch content), Pareto (t_prev,TT_closed)
//  - Dominance class 3a: merge dominates seal under a provably-safe condition
//  - Adjacent-batch interchange dominance (early-prunes doomed seal children)
//  - Adaptive BFS & DFS node selection (early incumbent + bounded memory)
//  Reports proven global lower bound + optimality gap (useful when timing out).
//  Self-contained.  Build: g++ -std=c++17 -O2 -o oracle oracle.cpp
//  Run: ./oracle <inst> [tl] [N_MAX] [N_MIN] [interch 0/1] [pos 0/1] [gamma] [qfrac]
//  Model: P(B)=S+V*sum vol+U*max h ; batch feasible iff sum(l*w) <= L*W.
// =============================================================================
#include <iostream>
#include <fstream>
#include <vector>
#include <string>
#include <algorithm>
#include <queue>
#include <unordered_map>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <chrono>
#include <numeric>
#include <utility>
#if defined(_MSC_VER)
  #include <intrin.h>
  static inline int popcount32(uint32_t x){ return (int)__popcnt(x); }
#else
  static inline int popcount32(uint32_t x){ return __builtin_popcount(x); }
#endif
using namespace std;

struct Inst { int n=0; double S=0,V=0,U=0,Aplat=0; vector<double> vol,h,area,d; };

static bool readInst(const string& fn, Inst& I){
    ifstream f(fn); if(!f) return false;
    int tm,tp,nm,np; if(!(f>>tm>>tp>>nm>>np)) return false;
    int mid,mnum; double scan,recoat,setup,L,W,Hh;
    f>>mid>>mnum>>scan>>recoat>>setup>>L>>W>>Hh;
    I.n=np; I.S=setup; I.V=scan; I.U=recoat; I.Aplat=L*W;
    I.vol.assign(np,0); I.h.assign(np,0); I.area.assign(np,0); I.d.assign(np,0);
    for(int i=0;i<np;i++){ int id,num,ori; double v,l,w,hh,sup; f>>id>>num>>ori>>v>>l>>w>>hh>>sup; I.vol[i]=v; I.h[i]=hh; I.area[i]=l*w; }
    string tok;
    if(f>>tok){ try{ double first=stod(tok); I.d[0]=first; for(int i=1;i<np;i++) f>>I.d[i]; } catch(...){ for(int i=0;i<np;i++) f>>I.d[i]; } }
    return true;
}

struct Node {
    uint32_t sched=0, open=0;
    double tprev=0, TTcl=0, LB=0;
    double oVol=0, oH=0, oArea=0; int oMax=-1;
    uint32_t prevMask=0; double prevStart=0;
};
static const double EPS=1e-9;

int main(int argc,char**argv){
    if(argc<2){ fprintf(stderr,"usage: %s inst [tl] [NMAX] [NMIN] [interch] [pos]\n",argv[0]); return 1; }
    string fn=argv[1]; double TL=(argc>2)?atof(argv[2]):120.0;
    Inst I; if(!readInst(fn,I)){ fprintf(stderr,"cannot read %s\n",fn.c_str()); return 1; }
    const int n=I.n; const double S=I.S,V=I.V,U=I.U,A=I.Aplat;
    const uint32_t FULL=(n>=32)?0xffffffffu:((1u<<n)-1u);
    auto& vol=I.vol; auto& hh=I.h; auto& ar=I.area; auto& dd=I.d;

    double UB;
    { vector<int> ord(n); iota(ord.begin(),ord.end(),0); sort(ord.begin(),ord.end(),[&](int a,int b){return dd[a]<dd[b];});
      vector<vector<int>> bs; vector<int> cur; double ca=0;
      for(int p:ord){ if(!cur.empty()&&ca+ar[p]>A+EPS){bs.push_back(cur);cur.clear();ca=0;} cur.push_back(p); ca+=ar[p]; }
      if(!cur.empty()) bs.push_back(cur);
      double t=0,tt=0; for(auto&b:bs){ double v=0,H=0; for(int p:b){v+=vol[p]; H=max(H,hh[p]);} t+=S+V*v+U*H; for(int p:b) tt+=max(0.0,t-dd[p]); } UB=tt; }
    const double initUBval=UB;

    size_t N_MAX=200000,N_MIN=50000;
    if(argc>3) N_MAX=(size_t)atoll(argv[3]);
    if(argc>4) N_MIN=(size_t)atoll(argv[4]);
    bool useInterch=(argc>5)?(atoi(argv[5])!=0):true;
    bool usePos    =(argc>6)?(atoi(argv[6])!=0):true;
    double GAMMA   =(argc>7)?atof(argv[7]):0.0;
    double QFRAC   =(argc>8)?atof(argv[8]):0.5; // compute pos only if |R| >= QFRAC*n
    size_t FRONT_CAP=(argc>9)?(size_t)atoll(argv[9]):25000000ULL; // max #keys in dominance frontier (memory cap)

    auto Pof=[&](uint32_t m){ double v=0,H=0; for(int i=0;i<n;i++) if(m>>i&1){v+=vol[i];H=max(H,hh[i]);} return S+V*v+U*H; };
    auto tardAt=[&](uint32_t m,double C){ double t=0; for(int i=0;i<n;i++) if(m>>i&1) t+=max(0.0,C-dd[i]); return t; };

    // reusable buffers for LB_pos
    vector<double> ra,rv,rH,rd,pH; ra.reserve(n); rv.reserve(n); rH.reserve(n); rd.reserve(n); pH.reserve(n+1);

    // LB_pos: positional lower bound on tardiness of the UNASSIGNED set (doc Incremental_Branching_Bounds.md, sec 4.1 "iron")
    auto lbPos=[&](const Node& c)->double{
        uint32_t un=FULL & ~c.sched;
        ra.clear(); rv.clear(); rH.clear(); rd.clear();
        for(int j=0;j<n;j++) if(un>>j&1){ ra.push_back(ar[j]); rv.push_back(vol[j]); rH.push_back(hh[j]); rd.push_back(dd[j]); }
        int q=(int)ra.size(); if(q==0) return 0.0;
        sort(ra.begin(),ra.end()); sort(rv.begin(),rv.end()); sort(rH.begin(),rH.end()); sort(rd.begin(),rd.end());
        bool hasOpen=(c.open!=0);
        double a_r=hasOpen?c.oArea:0.0, v_r=hasOpen?c.oVol:0.0, h_r=hasOpen?c.oH:0.0, F=c.tprev;
        pH.assign(q+1,0.0); for(int i=0;i<q;i++) pH[i+1]=pH[i]+rH[i];
        int r0=0; if(hasOpen){ while(r0<q && rH[r0]<=h_r+EPS) r0++; }
        double pos=0.0, Apre=0.0, Vpre=0.0;
        for(int k=1;k<=q;k++){
            Apre+=ra[k-1]; Vpre+=rv[k-1];
            long long betaArea=(long long)ceil((a_r+Apre)/A - 1e-6); if(betaArea<1) betaArea=1;
            long long cap=k+(hasOpen?1:0);
            long long beta=min(cap,betaArea);          // also == tilde-beta since beta<=cap=m_k
            double largest = hasOpen ? max(rH[k-1],h_r) : rH[k-1];
            long long s=beta-1; double sumSmall=0.0;   // sum of (beta-1) smallest tokens of {rH[0..k-1]} U {h_r?}
            if(s>0){
                if(!hasOpen) sumSmall=pH[s];
                else { int p=(int)min((long long)k,(long long)r0); if(s<=p) sumSmall=pH[s]; else sumSmall=pH[s-1]+h_r; }
            }
            double H_k=largest+sumSmall;
            double C_k=F + (double)beta*S + V*(v_r+Vpre) + U*H_k;
            pos += max(0.0, C_k - rd[k-1]);
        }
        return pos;
    };

    auto computeLB=[&](const Node& c)->double{
        uint32_t un=FULL & ~c.sched;
        double base=c.TTcl;
        double Cnow = (c.open==0) ? c.tprev : c.tprev + S + V*c.oVol + U*c.oH;
        if(c.open!=0) for(int i=0;i<n;i++) if(c.open>>i&1) base+=max(0.0,Cnow-dd[i]); // open-batch floor
        double parR=0.0;
        for(int j=0;j<n;j++) if(un>>j&1){
            double cc=Cnow + S + V*vol[j] + U*hh[j];
            if(c.open!=0 && j>c.oMax && c.oArea+ar[j]<=A+EPS){
                double merge_c=c.tprev + S + V*(c.oVol+vol[j]) + U*max(c.oH,hh[j]);
                cc=min(cc,merge_c);
            }
            parR+=max(0.0,cc-dd[j]);
        }
        double best=parR;
        int qrem=popcount32(un);
        if(usePos && qrem >= QFRAC*n && (base+parR) >= GAMMA*UB){ double posR=lbPos(c); if(posR>best) best=posR; }
        return base+best;
    };

    unordered_map<uint64_t,vector<pair<double,double>>> frontier; frontier.reserve(1<<16);
    auto domCheckInsert=[&](uint32_t sched,uint32_t open,double tp,double tt)->bool{
        uint64_t key=((uint64_t)sched<<32)|open;
        auto it=frontier.find(key);
        if(it==frontier.end()){
            if(frontier.size()>=FRONT_CAP) return false;   // memory cap: stop adding new keys (dominance optional -> still correct)
            frontier.emplace(key, vector<pair<double,double>>{{tp,tt}});
            return false;
        }
        auto& fr=it->second;
        for(auto&pr:fr) if(pr.first<=tp+EPS && pr.second<=tt+EPS) return true;
        vector<pair<double,double>> keep; keep.reserve(fr.size()+1);
        for(auto&pr:fr) if(!(tp<=pr.first+EPS && tt<=pr.second+EPS)) keep.push_back(pr);
        keep.emplace_back(tp,tt); fr.swap(keep); return false;
    };

    struct Cmp{ bool operator()(const Node&a,const Node&b)const{ return a.LB>b.LB; } };
    priority_queue<Node,vector<Node>,Cmp> pq; vector<Node> stk; bool useBFS=true;
    long long popped=0,generated=0,leaves=0,lbPrune=0,domPrune=0,a3Prune=0,interchPrune=0,dives=0;
    Node root; root.LB=computeLB(root); pq.push(root);
    auto t0=chrono::steady_clock::now(); bool timedOut=false;

    auto processChild=[&](Node c, vector<Node>& out){
        ++generated;
        if(c.sched==FULL){ double Cc=c.tprev+S+V*c.oVol+U*c.oH; double obj=c.TTcl; for(int i=0;i<n;i++) if(c.open>>i&1) obj+=max(0.0,Cc-dd[i]); ++leaves; if(obj<UB-EPS) UB=obj; return; }
        c.LB=computeLB(c);
        if(c.LB>=UB-EPS){ ++lbPrune; return; }
        if(domCheckInsert(c.sched,c.open,c.tprev,c.TTcl)){ ++domPrune; return; }
        out.push_back(std::move(c));
    };

    while(!pq.empty()||!stk.empty()){
        if((popped&1023)==0){ double el=chrono::duration<double>(chrono::steady_clock::now()-t0).count(); if(el>TL){timedOut=true;break;} }
        size_t sz=pq.size()+stk.size();
        if(sz>N_MAX) useBFS=false; else if(sz<N_MIN) useBFS=true;
        Node cur;
        if(useBFS){ if(!pq.empty()){cur=pq.top();pq.pop();} else {cur=stk.back();stk.pop_back();} }
        else { if(!stk.empty()){cur=stk.back();stk.pop_back();} else {cur=pq.top();pq.pop();++dives;} }
        if(cur.LB>=UB-EPS) continue;
        ++popped;
        bool sealDominated=false;
        if(useInterch && cur.open!=0 && cur.prevMask!=0){
            double tau=cur.prevStart, Px=Pof(cur.prevMask), Py=Pof(cur.open);
            double tXY=tardAt(cur.prevMask,tau+Px)+tardAt(cur.open,tau+Px+Py);
            double tYX=tardAt(cur.open,tau+Py)+tardAt(cur.prevMask,tau+Px+Py);
            if(tYX<tXY-EPS) sealDominated=true;
        }
        uint32_t un=FULL & ~cur.sched;
        double Cnow=(cur.open==0)?cur.tprev:cur.tprev+S+V*cur.oVol+U*cur.oH;
        vector<Node> kids;
        for(int j=0;j<n;j++) if(un>>j&1){
            bool t2feas=(cur.open!=0)&&(j>cur.oMax)&&(cur.oArea+ar[j]<=A+EPS);
            bool skipSeal=false;
            if(t2feas){
                double sumA=0,sumV=0,maxH=max(cur.oH,hh[j]);
                for(int k=0;k<n;k++) if((un>>k&1)&&k>j){ sumA+=ar[k]; sumV+=vol[k]; maxH=max(maxH,hh[k]); }
                if(cur.oArea+ar[j]+sumA<=A+EPS){
                    double Cbar=cur.tprev+S+V*(cur.oVol+vol[j]+sumV)+U*maxH; bool allSafe=true;
                    for(int i=0;i<n;i++) if((cur.open>>i&1)&&dd[i]<Cbar-EPS){allSafe=false;break;}
                    if(allSafe) skipSeal=true;
                }
            }
            if(skipSeal){ ++a3Prune; }
            else if(sealDominated){ ++interchPrune; }
            else {
                Node c=cur;
                if(cur.open==0){ c.tprev=cur.tprev; c.TTcl=cur.TTcl; c.prevMask=0; c.prevStart=0; }
                else { double sealT=0; for(int i=0;i<n;i++) if(cur.open>>i&1) sealT+=max(0.0,Cnow-dd[i]); c.tprev=Cnow; c.TTcl=cur.TTcl+sealT; c.prevMask=cur.open; c.prevStart=cur.tprev; }
                c.sched=cur.sched|(1u<<j); c.open=(1u<<j); c.oVol=vol[j]; c.oH=hh[j]; c.oArea=ar[j]; c.oMax=j;
                processChild(c,kids);
            }
            if(t2feas){
                Node c=cur;
                c.sched=cur.sched|(1u<<j); c.open=cur.open|(1u<<j);
                c.oVol=cur.oVol+vol[j]; c.oH=max(cur.oH,hh[j]); c.oArea=cur.oArea+ar[j]; c.oMax=j;
                c.tprev=cur.tprev; c.TTcl=cur.TTcl; c.prevMask=cur.prevMask; c.prevStart=cur.prevStart;
                processChild(c,kids);
            }
        }
        if(useBFS){ for(auto&k:kids) pq.push(std::move(k)); }
        else { sort(kids.begin(),kids.end(),[](const Node&a,const Node&b){return a.LB>b.LB;}); for(auto&k:kids) stk.push_back(std::move(k)); }
    }
    double glb=UB;
    if(!pq.empty()) glb=min(glb,pq.top().LB);
    for(auto&nd:stk) glb=min(glb,nd.LB);
    double gap = (UB>1e-9)? 100.0*(UB-glb)/UB : 0.0;
    double sec=chrono::duration<double>(chrono::steady_clock::now()-t0).count();
    printf("RESULT n=%d initUB=%.6g obj=%.6g %s popped=%lld generated=%lld leaves=%lld lb_prune=%lld dom_prune=%lld a3_prune=%lld interch_prune=%lld dives=%lld time_s=%.4f lb=%.6g gap=%.2f%%\n",
           n,initUBval,UB,(timedOut?"(TIMEOUT)":"proven"),popped,generated,leaves,lbPrune,domPrune,a3Prune,interchPrune,dives,sec,glb,gap);
    return 0;
}
