#!/usr/bin/env python3
"""Generates deterministic cross-check scenarios (ZEST, fourTwo, FT) shared by the C# port and OPI in R."""
import json, math, random, sys

def pnorm_pdf(x, m, s):
    return math.exp(-0.5 * ((x - m) / s) ** 2) / (s * math.sqrt(2 * math.pi))

def main(out):
    rnd = random.Random(20261017)
    sc = []
    dom = list(range(-5, 36))
    for k in range(60):
        kind = ["uniform", "normal", "bimodal"][k % 3]
        mu = rnd.uniform(5, 30)
        if kind == "uniform":
            prior = [1.0] * len(dom)
        elif kind == "normal":
            prior = [pnorm_pdf(t, mu, 4) for t in dom]
        else:
            prior = [4 * pnorm_pdf(t, mu, 3) + pnorm_pdf(t, -7.5, 6) + 0.001 for t in dom]
        stop = [("S", 1.5), ("S", 2.0), ("H", 3.0), ("N", 6)][k % 4]
        choice = ["mean", "mean", "mode", "median"][k % 4]
        s = {"Id": f"zest-{k}", "Algorithm": "ZEST", "Domain": dom, "Prior": prior,
             "Fpr": 0.03, "Fnr": rnd.choice([0.01, 0.03, 0.05]), "LikelihoodSd": rnd.choice([1.0, 1.5, 2.0]),
             "StopType": stop[0], "StopValue": stop[1], "MinStimulus": 0, "MaxStimulus": 30,
             "StimChoice": choice, "TrueThreshold": round(rnd.uniform(-8, 33), 1)}
        if k % 2 == 1:
            s["Responses"] = [rnd.random() < 0.5 for _ in range(100)]
        sc.append(s)
    for alg in ["fourTwo", "FT"]:
        for k in range(40):
            s = {"Id": f"{alg}-{k}", "Algorithm": alg, "MinStimulus": 0, "MaxStimulus": 40,
                 "Est": rnd.randint(0, 40), "TrueThreshold": round(rnd.uniform(-5, 45), 1)}
            if k % 2 == 1:
                s["Responses"] = [rnd.random() < 0.5 for _ in range(100)]
            sc.append(s)
    json.dump(sc, open(out, "w"), indent=1)
    print(f"wrote {len(sc)} scenarios to {out}")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "scenarios.json")
