#include "prize_oracle.h"
#include <algorithm>
#include <limits>
#include <cstdint>
#include <vector>
#include <unordered_map>

namespace {

constexpr double INF = std::numeric_limits<double>::infinity();

struct PCtx {
    int k = 0;
    std::vector<double> area_, vol_, hh_, dd_, uu_;
    double S=0, V=0, U=0, cap=0;
    uint64_t mandMask = 0;
    double incumbent = INF;
    uint64_t bestMask = 0;
    // dominance: scheduled-set mask -> nondominated (C,G) labels
    std::unordered_map<uint64_t, std::vector<std::pair<double,double>>> dom;
};

// rewards-aware parallel lower bound on the additional contribution from R,
// given current completion C:
//   each included part j completes >= C + S + V v_j + U h_j  => tardiness >= tmin_j
//   mandatory j : must take (tmin_j - u_j); free j : take min(0, tmin_j - u_j)
double remainderLB(const PCtx& c, uint64_t R, double C) {
    double s = 0.0;
    for (int j = 0; j < c.k; ++j) if (R & (uint64_t(1)<<j)) {
        double tmin = C + c.S + c.V*c.vol_[j] + c.U*c.hh_[j] - c.dd_[j];
        if (tmin < 0) tmin = 0;
        double contrib = tmin - c.uu_[j];
        if (c.mandMask & (uint64_t(1)<<j)) s += contrib;          // mandatory: must include
        else                               s += std::min(0.0, contrib); // free: include or drop
    }
    return s;
}

// returns true if (C,G) is dominated by an existing label for `scheduled`;
// otherwise inserts it (removing labels it dominates) and returns false.
bool domCheckInsert(PCtx& c, uint64_t scheduled, double C, double G) {
    auto& vec = c.dom[scheduled];
    for (auto& p : vec)
        if (p.first <= C + 1e-9 && p.second <= G + 1e-9) return true;  // dominated
    vec.erase(std::remove_if(vec.begin(), vec.end(),
              [&](const std::pair<double,double>& p){ return C <= p.first+1e-9 && G <= p.second+1e-9; }),
              vec.end());
    vec.push_back({C, G});
    return false;
}

void dfs(PCtx& c, uint64_t R, double C, double G, uint64_t scheduled) {
    // candidate: may stop here iff no mandatory part remains in R
    if ((R & c.mandMask) == 0 && G < c.incumbent - 1e-12) {
        c.incumbent = G; c.bestMask = scheduled;
    }
    // bound prune
    if (G + remainderLB(c, R, C) >= c.incumbent - 1e-12) return;
    if (R == 0) return;
    // dominance prune (after candidate update, so correctness preserved)
    if (domCheckInsert(c, scheduled, C, G)) return;

    for (uint64_t B = R; B; B = (B - 1) & R) {
        double a = 0, vol = 0, mh = 0;
        for (int j = 0; j < c.k; ++j) if (B & (uint64_t(1)<<j)) {
            a += c.area_[j]; vol += c.vol_[j];
            if (c.hh_[j] > mh) mh = c.hh_[j];
        }
        if (a > c.cap + 1e-9) continue;
        double Cn = C + c.S + c.V*vol + c.U*mh;
        double add = 0;
        for (int j = 0; j < c.k; ++j) if (B & (uint64_t(1)<<j))
            add += std::max(0.0, Cn - c.dd_[j]) - c.uu_[j];
        dfs(c, R & ~B, Cn, G + add, scheduled | B);
    }
}

// EDD-greedy feasible solution over ALL parts (one valid A = full set)
double greedyAll(const PCtx& c) {
    std::vector<int> ord(c.k); for (int i=0;i<c.k;++i) ord[i]=i;
    std::sort(ord.begin(), ord.end(), [&](int a,int b){ return c.dd_[a] < c.dd_[b]; });
    double C=0, G=0, area=0, vol=0, mh=0; std::vector<int> cur;
    auto close=[&](){
        if(cur.empty()) return;
        double Cn=C + c.S + c.V*vol + c.U*mh;
        for(int j:cur) G += std::max(0.0, Cn - c.dd_[j]) - c.uu_[j];
        C=Cn; cur.clear(); area=vol=mh=0;
    };
    for(int j: ord){
        if(area + c.area_[j] > c.cap + 1e-9) close();
        cur.push_back(j); area+=c.area_[j]; vol+=c.vol_[j]; mh=std::max(mh,c.hh_[j]);
    }
    close();
    return G;
}

} // namespace

PrizeResult prizeCollectSingleMachine(
    const std::vector<int>& parts,
    const std::vector<double>& l, const std::vector<double>& w,
    const std::vector<double>& h, const std::vector<double>& v,
    const std::vector<double>& d,
    double S, double Vc, double Uc, double area,
    const std::vector<double>& u,
    const std::vector<int>& mandatory)
{
    PCtx c;
    c.k = (int)parts.size();
    c.S = S; c.V = Vc; c.U = Uc; c.cap = area;
    c.area_.resize(c.k); c.vol_.resize(c.k); c.hh_.resize(c.k);
    c.dd_.resize(c.k);   c.uu_.resize(c.k);
    std::vector<int> g2l(parts.empty()?0:(*std::max_element(parts.begin(),parts.end())+1), -1);
    for (int i = 0; i < c.k; ++i) {
        int id = parts[i];
        c.area_[i]=l[id]*w[id]; c.vol_[i]=v[id]; c.hh_[i]=h[id];
        c.dd_[i]=d[id]; c.uu_[i]=u[id]; g2l[id]=i;
    }
    for (int id : mandatory) if (id < (int)g2l.size() && g2l[id] >= 0)
        c.mandMask |= (uint64_t(1) << g2l[id]);

    uint64_t full = (c.k >= 64) ? ~uint64_t(0) : ((uint64_t(1) << c.k) - 1);
    c.incumbent = (c.mandMask == 0) ? 0.0 : INF;
    c.bestMask  = (c.mandMask == 0) ? 0ULL : full;     // provisional feasible
    // greedy(all) as an initial incumbent (always feasible: includes everything incl. mandatory)
    double gv = greedyAll(c);
    if (gv < c.incumbent - 1e-12) { c.incumbent = gv; c.bestMask = full; }

    dfs(c, full, 0.0, 0.0, 0ULL);

    PrizeResult r;
    r.value = c.incumbent;
    for (int i = 0; i < c.k; ++i) if (c.bestMask & (uint64_t(1)<<i)) r.chosen.push_back(parts[i]);
    return r;
}
