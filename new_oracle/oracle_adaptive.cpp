#include <bits/stdc++.h>
using namespace std;

struct Inst {
    int n = 0;
    double S = 0, V = 0, U = 0, Aplat = 0;
    vector<double> vol, h, area, d;
};

static bool readInst(const string& fn, Inst& I) {
    ifstream f(fn);
    if (!f) return false;
    int tm, tp, nm, np;
    if (!(f >> tm >> tp >> nm >> np)) return false;
    int mid, mnum; double scan, recoat, setup, L, W, Hh;
    f >> mid >> mnum >> scan >> recoat >> setup >> L >> W >> Hh;
    I.n = np; I.S = setup; I.V = scan; I.U = recoat; I.Aplat = L * W;
    I.vol.assign(np, 0); I.h.assign(np, 0); I.area.assign(np, 0); I.d.assign(np, 0);
    for (int i = 0; i < np; ++i) {
        int id, num, ori; double v; double l, w, hh, sup;
        f >> id >> num >> ori >> v >> l >> w >> hh >> sup;
        I.vol[i] = v; I.h[i] = hh; I.area[i] = l * w;
    }
    string tok;
    if (f >> tok) {
        try { double first = stod(tok); I.d[0] = first; for (int i = 1; i < np; ++i) f >> I.d[i]; }
        catch (...) { for (int i = 0; i < np; ++i) f >> I.d[i]; }
    }
    return true;
}

struct Node {
    uint32_t sched = 0, open = 0;
    double tprev = 0, TTcl = 0, LB = 0;
    double oVol = 0, oH = 0, oArea = 0;
    int oMax = -1;
};

static const double EPS = 1e-9;

int main(int argc, char** argv) {
    if (argc < 2) { fprintf(stderr, "usage: %s instance [tl] [NMAX] [NMIN]\n", argv[0]); return 1; }
    string fn = argv[1];
    double TL = (argc > 2) ? atof(argv[2]) : 120.0;
    Inst I;
    if (!readInst(fn, I)) { fprintf(stderr, "cannot read %s\n", fn.c_str()); return 1; }
    const int n = I.n;
    const double S = I.S, V = I.V, U = I.U, A = I.Aplat;
    const uint32_t FULL = (n >= 32) ? 0xffffffffu : ((1u << n) - 1u);
    auto& vol = I.vol; auto& hh = I.h; auto& ar = I.area; auto& dd = I.d;

    double UB;
    {
        vector<int> ord(n); iota(ord.begin(), ord.end(), 0);
        sort(ord.begin(), ord.end(), [&](int a, int b){ return dd[a] < dd[b]; });
        vector<vector<int>> batches; vector<int> cur; double ca = 0;
        for (int p : ord) {
            if (!cur.empty() && ca + ar[p] > A + EPS) { batches.push_back(cur); cur.clear(); ca = 0; }
            cur.push_back(p); ca += ar[p];
        }
        if (!cur.empty()) batches.push_back(cur);
        double t = 0, tt = 0;
        for (auto& b : batches) {
            double v = 0, H = 0; for (int p : b) { v += vol[p]; H = max(H, hh[p]); }
            t += S + V * v + U * H;
            for (int p : b) tt += max(0.0, t - dd[p]);
        }
        UB = tt;
    }
    const double initUBval = UB;

    auto computeLB = [&](const Node& c) -> double {
        double lb = c.TTcl;
        uint32_t un = FULL & ~c.sched;
        if (c.open == 0) {
            for (int j = 0; j < n; ++j) if (un >> j & 1) { double cc = c.tprev + S + V*vol[j] + U*hh[j]; lb += max(0.0, cc - dd[j]); }
            return lb;
        }
        double Cnow = c.tprev + S + V*c.oVol + U*c.oH;
        for (int i = 0; i < n; ++i) if (c.open >> i & 1) lb += max(0.0, Cnow - dd[i]);
        for (int j = 0; j < n; ++j) if (un >> j & 1) {
            double new_c = Cnow + S + V*vol[j] + U*hh[j];
            double cc = new_c;
            if (j > c.oMax && c.oArea + ar[j] <= A + EPS) {
                double merge_c = c.tprev + S + V*(c.oVol + vol[j]) + U*max(c.oH, hh[j]);
                cc = min(new_c, merge_c);
            }
            lb += max(0.0, cc - dd[j]);
        }
        return lb;
    };

    unordered_map<uint64_t, vector<pair<double,double>>> frontier;
    frontier.reserve(1 << 16);
    auto domCheckInsert = [&](uint32_t sched, uint32_t open, double tp, double tt) -> bool {
        uint64_t key = ((uint64_t)sched << 32) | open;
        auto& fr = frontier[key];
        for (auto& pr : fr) if (pr.first <= tp + EPS && pr.second <= tt + EPS) return true;
        vector<pair<double,double>> keep; keep.reserve(fr.size() + 1);
        for (auto& pr : fr) if (!(tp <= pr.first + EPS && tt <= pr.second + EPS)) keep.push_back(pr);
        keep.emplace_back(tp, tt); fr.swap(keep);
        return false;
    };

    struct Cmp { bool operator()(const Node& a, const Node& b) const { return a.LB > b.LB; } };
    priority_queue<Node, vector<Node>, Cmp> pq;
    vector<Node> stk;
    size_t N_MAX = 200000, N_MIN = 50000;
    if (argc > 3) N_MAX = (size_t)atoll(argv[3]);
    if (argc > 4) N_MIN = (size_t)atoll(argv[4]);
    bool useBFS = true;

    long long popped=0, generated=0, leaves=0, lbPrune=0, domPrune=0, a3Prune=0, dives=0;
    Node root; root.LB = computeLB(root); pq.push(root);
    auto t0 = chrono::steady_clock::now();
    bool timedOut = false;

    auto processChild = [&](Node c, vector<Node>& out) {
        ++generated;
        if (c.sched == FULL) {
            double Cc = c.tprev + S + V*c.oVol + U*c.oH;
            double obj = c.TTcl;
            for (int i = 0; i < n; ++i) if (c.open >> i & 1) obj += max(0.0, Cc - dd[i]);
            ++leaves; if (obj < UB - EPS) UB = obj; return;
        }
        c.LB = computeLB(c);
        if (c.LB >= UB - EPS) { ++lbPrune; return; }
        if (domCheckInsert(c.sched, c.open, c.tprev, c.TTcl)) { ++domPrune; return; }
        out.push_back(std::move(c));
    };

    while (!pq.empty() || !stk.empty()) {
        if ((popped & 1023) == 0) {
            double el = chrono::duration<double>(chrono::steady_clock::now() - t0).count();
            if (el > TL) { timedOut = true; break; }
        }
        size_t sz = pq.size() + stk.size();
        if (sz > N_MAX) useBFS = false; else if (sz < N_MIN) useBFS = true;
        Node cur;
        if (useBFS) { if (!pq.empty()) { cur = pq.top(); pq.pop(); } else { cur = stk.back(); stk.pop_back(); } }
        else { if (!stk.empty()) { cur = stk.back(); stk.pop_back(); } else { cur = pq.top(); pq.pop(); ++dives; } }
        if (cur.LB >= UB - EPS) continue;
        ++popped;
        uint32_t un = FULL & ~cur.sched;
        double Cnow = (cur.open == 0) ? cur.tprev : cur.tprev + S + V*cur.oVol + U*cur.oH;
        vector<Node> kids;
        for (int j = 0; j < n; ++j) if (un >> j & 1) {
            bool t2feas = (cur.open != 0) && (j > cur.oMax) && (cur.oArea + ar[j] <= A + EPS);
            bool skipSeal = false;
            if (t2feas) {
                double sumA=0, sumV=0, maxH=max(cur.oH, hh[j]);
                for (int k = 0; k < n; ++k) if ((un >> k & 1) && k > j) { sumA += ar[k]; sumV += vol[k]; maxH = max(maxH, hh[k]); }
                if (cur.oArea + ar[j] + sumA <= A + EPS) {
                    double Cbar = cur.tprev + S + V*(cur.oVol + vol[j] + sumV) + U*maxH;
                    bool allSafe = true;
                    for (int i = 0; i < n; ++i) if ((cur.open >> i & 1) && dd[i] < Cbar - EPS) { allSafe = false; break; }
                    if (allSafe) skipSeal = true;
                }
            }
            if (!skipSeal) {
                Node c = cur;
                if (cur.open == 0) { c.tprev = cur.tprev; c.TTcl = cur.TTcl; }
                else { double sealT = 0; for (int i = 0; i < n; ++i) if (cur.open >> i & 1) sealT += max(0.0, Cnow - dd[i]); c.tprev = Cnow; c.TTcl = cur.TTcl + sealT; }
                c.sched = cur.sched | (1u << j); c.open = (1u << j);
                c.oVol = vol[j]; c.oH = hh[j]; c.oArea = ar[j]; c.oMax = j;
                processChild(c, kids);
            } else ++a3Prune;
            if (t2feas) {
                Node c = cur;
                c.sched = cur.sched | (1u << j); c.open = cur.open | (1u << j);
                c.oVol = cur.oVol + vol[j]; c.oH = max(cur.oH, hh[j]); c.oArea = cur.oArea + ar[j]; c.oMax = j;
                c.tprev = cur.tprev; c.TTcl = cur.TTcl;
                processChild(c, kids);
            }
        }
        if (useBFS) { for (auto& k : kids) pq.push(std::move(k)); }
        else { sort(kids.begin(), kids.end(), [](const Node& a, const Node& b){ return a.LB > b.LB; }); for (auto& k : kids) stk.push_back(std::move(k)); }
    }

    double sec = chrono::duration<double>(chrono::steady_clock::now() - t0).count();
    printf("RESULT n=%d initUB=%.6g obj=%.6g %s popped=%lld generated=%lld leaves=%lld lb_prune=%lld dom_prune=%lld a3_prune=%lld dives=%lld time_s=%.4f\n",
           n, initUBval, UB, (timedOut ? "(TIMEOUT)" : "proven"),
           popped, generated, leaves, lbPrune, domPrune, a3Prune, dives, sec);
    return 0;
}
