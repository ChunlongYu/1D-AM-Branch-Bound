#include "ParallelBranchBound.h"
#include <iostream>
#include <vector>
#include <algorithm>
#include <limits>
#include <random>

std::vector<double> D,l,w,h,v,ST,VT,UT,Lp,Wp; double area;

// oracle-free exact single machine: min total tardiness over ordered feasible batch seqs
double bestTT;
void rec(const std::vector<int>& rem,double t,double tt){
    if(tt>=bestTT) return;
    if(rem.empty()){ bestTT=std::min(bestTT,tt); return; }
    int n=rem.size();
    for(unsigned msk=1;msk<(1u<<n);++msk){
        double a=0,vol=0,mh=0; std::vector<int> batch,left;
        for(int i=0;i<n;++i){ if(msk&(1u<<i)){a+=l[rem[i]]*w[rem[i]];vol+=v[rem[i]];mh=std::max(mh,h[rem[i]]);batch.push_back(rem[i]);} else left.push_back(rem[i]); }
        if(a>area+1e-9) continue;
        double ct=t+ST[0]+VT[0]*vol+UT[0]*mh,add=0;
        for(int j:batch) add+=std::max(0.0,ct-D[j]);
        rec(left,ct,tt+add);
    }
}
double phiExact(const std::vector<int>& Q){ if(Q.empty())return 0.0; bestTT=std::numeric_limits<double>::infinity(); rec(Q,0,0); return bestTT; }

double bruteBest; int n,M;
void assignRec(int idx,std::vector<std::vector<int>>& g){
    if(idx==n){ double tot=0; for(int m=0;m<M;++m) tot+=phiExact(g[m]); bruteBest=std::min(bruteBest,tot); return; }
    for(int m=0;m<M;++m){ g[m].push_back(idx); assignRec(idx+1,g); g[m].pop_back(); }
}

int main(){
    ST={2.0};VT={0.030864};UT={0.7};Lp={20};Wp={20};area=400;
    std::mt19937 rng(12345);
    int fails=0,cases=0;
    for(int trial=0;trial<6;++trial){
        n=5+ (trial%3); // 5,6,7
        std::uniform_real_distribution<double> dim(10,19), due(5,60), vol(30,160), hgt(2,33);
        l.assign(n,0);w.assign(n,0);h.assign(n,0);v.assign(n,0);D.assign(n,0);
        for(int j=0;j<n;++j){ l[j]=(int)dim(rng); w[j]=(int)dim(rng); h[j]=(int)hgt(rng); v[j]=vol(rng); D[j]=(int)due(rng); }
        // scale dims so a few parts fit (area 400, dims 10-19 -> 1-4 per batch)
        std::vector<int> parts(n); for(int j=0;j<n;++j)parts[j]=j;
        for(int Mm=1;Mm<=3;++Mm){
            M=Mm; bruteBest=std::numeric_limits<double>::infinity();
            std::vector<std::vector<int>> g(M); assignRec(0,g);
            PBBParams p; p.M=Mm; p.time_limit=0; p.strong_branch_candidates=0;
            auto res=solveParallelMachine(parts,D,ST,VT,UT,Lp,Wp,l,w,h,v,p);
            bool ok=std::abs(res.first.total_tardiness-bruteBest)<1e-4;
            cases++; if(!ok)fails++;
            std::cout<<"trial"<<trial<<" n="<<n<<" M="<<Mm
                     <<"  BBB="<<res.first.total_tardiness<<"  brute="<<bruteBest
                     <<"  "<<(ok?"OK":"*** FAIL ***")
                     <<"  [nodes="<<res.second.total_nodes<<" oracle="<<res.second.oracle_calls<<"]\n";
        }
    }
    std::cout<<"\n"<<(cases-fails)<<"/"<<cases<<" cases passed\n";
    return fails==0?0:1;
}
