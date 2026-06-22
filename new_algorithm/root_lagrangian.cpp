// =============================================================================
//  Step 2: root-node Lagrangian bound via the prize-collecting oracle.
//  L(u) = sum_j u_j + M * cbar*(u),  cbar*(u)=min_P[Phi(P)-sum_{j in P}u_j]
//  (root: identical empty machines, no mandatory base -> all M subproblems equal).
//  Subgradient: g_j = 1 - M*[j in P*];  Polyak step with known/target UB.
//  Reproduces the enumeration-based minimal-validation numbers, but the
//  subproblem is now solved by prizeCollectSingleMachine (no full subset enum).
// =============================================================================
#include "prize_oracle.h"
#include "InstanceData.h"
#include <bits/stdc++.h>
using namespace std;

int main(int argc, char** argv){
    string inst = argc>1 ? argv[1] : "10part";
    int    iters = argc>2 ? atoi(argv[2]) : 300;
    string path = "../data/" + inst + ".txt";

    MachineInfo m; vector<PartInfo> ps; PartLists pl;
    if(!readMachineAndParts(path, m, ps, pl)){ printf("cannot read %s\n", path.c_str()); return 1; }
    int n = ps.size();
    vector<double> l=pl.lengths, w=pl.widths, h=pl.heights, v=pl.volumes, d=pl.due_dates;
    double S=m.setup_time, V=m.scanning_speed, U=m.recoater_speed, area=m.length*m.width;
    vector<int> parts; for(int i=0;i<n;++i) parts.push_back(i);

    // known optima (from experiments/results/bb_results.csv) for the comparison
    map<pair<string,int>,double> OPT = {
        {{"10part",2},45.6258},{{"10part",3},22.9517},{{"10part",4},20.1024},
        {{"11part",2},39.8947},{{"11part",3},19.9492},{{"11part",4},17.4074},
        {{"12part",2},51.5220},{{"12part",3},29.3207},{{"12part",4},19.8653},
    };
    // current root bound = sum of singleton tardiness
    double rootCur=0; for(int j=0;j<n;++j) rootCur+=max(0.0, S+V*v[j]+U*h[j]-d[j]);

    printf("instance=%s n=%d   current root bound (sum singleton)=%.3f\n", inst.c_str(), n, rootCur);
    for(int M=2; M<=4; ++M){
        double UB = OPT.count({inst,M}) ? OPT[{inst,M}] : -1;
        double target = (UB>0)? UB : (rootCur*3+1); // fallback target if opt unknown

        vector<double> u(n, 0.0);
        double bestL=-1e18; double rho=2.0; int stall=0;
        auto t0=chrono::steady_clock::now();
        long long calls=0;
        for(int it=0; it<iters; ++it){
            auto r = prizeCollectSingleMachine(parts,l,w,h,v,d,S,V,U,area,u,{});
            ++calls;
            double cbar = r.value;                  // <= 0
            double Lu = 0; for(double x:u) Lu+=x; Lu += M*cbar;
            if(Lu > bestL+1e-9){ bestL=Lu; stall=0; } else if(++stall>=20){ rho*=0.5; stall=0; }
            // subgradient g_j = 1 - M*[j in P*]
            vector<char> inP(n,0); for(int id:r.chosen) inP[id]=1;
            double nrm=0; vector<double> g(n);
            for(int j=0;j<n;++j){ g[j]=1.0 - M*inP[j]; nrm+=g[j]*g[j]; }
            if(nrm<1e-12) break;
            double step = rho*max(0.0,(target-Lu))/nrm; if(step<1e-9) step=1e-3;
            for(int j=0;j<n;++j) u[j]+=step*g[j];
        }
        double sec=chrono::duration<double>(chrono::steady_clock::now()-t0).count();
        if(UB>0)
            printf("  M=%d: OPT=%.3f  current=%.3f(%.0f%%)  Lagrangian_root=%.3f(%.1f%%)  [%lld calls, %.2fs, %.2fms/call]\n",
                   M, UB, rootCur, 100*rootCur/UB, bestL, 100*bestL/UB, calls, sec, 1000*sec/calls);
        else
            printf("  M=%d: current=%.3f  Lagrangian_root=%.3f  [%lld calls, %.2fs, %.2fms/call]\n",
                   M, rootCur, bestL, calls, sec, 1000*sec/calls);
    }
    return 0;
}
