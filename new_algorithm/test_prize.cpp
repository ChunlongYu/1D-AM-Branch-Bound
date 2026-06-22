#include "prize_oracle.h"
#include <bits/stdc++.h>
using namespace std;

// 7-part instance (first 7 parts of 15part), S/V/U/area as in the data
vector<double> l={3,10,7,5,4,8,7}, w={6,6,3,4,5,3,7},
               h={32,8,3,7,5,4,4}, v={90,150,30,62,72,50,78.6},
               d={15,43,11,23,14,38,35};
double S=2.0, V=0.030864, U=0.7, area=400.0;
int n=7;
vector<double> uu(7,0.0);

double bestPhi;
void recPhi(uint64_t rem, double C, double tt){
    if(tt>=bestPhi) return;
    if(rem==0){ bestPhi=min(bestPhi,tt); return; }
    for(uint64_t B=rem; B; B=(B-1)&rem){
        double a=0,vol=0,mh=0;
        for(int j=0;j<n;++j) if(B&(1ull<<j)){a+=l[j]*w[j];vol+=v[j];mh=max(mh,h[j]);}
        if(a>area+1e-9) continue;
        double Cn=C+S+V*vol+U*mh, add=0;
        for(int j=0;j<n;++j) if(B&(1ull<<j)) add+=max(0.0,Cn-d[j]);
        recPhi(rem&~B, Cn, tt+add);
    }
}
double phiA(uint64_t A){ if(A==0) return 0; bestPhi=1e18; recPhi(A,0,0); return bestPhi; }

double prizeBrute(uint64_t mandMask){
    double best = (mandMask==0)? 0.0 : 1e18;
    uint64_t full=(1ull<<n)-1;
    for(uint64_t A=0; A<=full; ++A){
        if((A & mandMask)!=mandMask) continue;       // A must contain mandatory
        double s=phiA(A);
        for(int j=0;j<n;++j) if(A&(1ull<<j)) s-=uu[j];
        best=min(best,s);
    }
    return best;
}

int main(){
    vector<int> parts; for(int i=0;i<n;++i)parts.push_back(i);
    mt19937 rng(2024);
    int fails=0, cases=0;
    vector<vector<int>> mandSets = {{}, {2}, {0,4}};
    for(int trial=0; trial<8; ++trial){
        // random rewards: mix of signs, scale ~ tardiness
        uniform_real_distribution<double> ud(-5.0, 20.0);
        for(int j=0;j<n;++j) uu[j]=round(ud(rng)*10)/10.0;
        for(auto& mand : mandSets){
            uint64_t mm=0; for(int id:mand) mm|=(1ull<<id);
            double brute = prizeBrute(mm);
            auto r = prizeCollectSingleMachine(parts,l,w,h,v,d,S,V,U,area,uu,mand);
            bool ok = fabs(brute - r.value) < 1e-6;
            cases++; if(!ok) fails++;
            // verify chosen set respects mandatory + recompute its objective
            uint64_t chosenMask=0; for(int id:r.chosen) chosenMask|=(1ull<<id);
            bool mandOk = (chosenMask & mm)==mm;
            printf("trial%d mand={%s}: brute=%.4f  oracle=%.4f  %s  |chosen|=%zu mandOk=%d\n",
                   trial, [&]{string s;for(int id:mand)s+=to_string(id)+",";return s;}().c_str(),
                   brute, r.value, ok?"OK":"*** FAIL ***", r.chosen.size(), (int)mandOk);
        }
    }
    printf("\n%d/%d cases passed\n", cases-fails, cases);
    return fails==0?0:1;
}
