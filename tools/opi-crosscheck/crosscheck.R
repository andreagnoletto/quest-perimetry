#!/usr/bin/env Rscript
# Compares the C# ports in /core with the reference implementations in OPI (Apache-2.0).
# Usage: Rscript crosscheck.R scenarios.json csharp_results.json <dir with OPI R sources>
# The OPI sources (zest.r, fourTwo.r, full_threshold.r) are sourced directly, so the full OPI package
# (and its dependencies) does not need to be installed. opiPresent is replaced by a deterministic stub.
suppressWarnings(suppressMessages({ ok <- requireNamespace("jsonlite", quietly = TRUE) }))
if (!ok) stop("package jsonlite is required")
args <- commandArgs(trailingOnly = TRUE)
scen <- jsonlite::fromJSON(args[1], simplifyVector = FALSE)
cs   <- jsonlite::fromJSON(args[2], simplifyVector = FALSE)
opi_dir <- args[3]
for (f in c("zest.r", "fourTwo.r", "full_threshold.r")) source(file.path(opi_dir, f))

.resp <- new.env()
opiPresent <- function(stim, nextStim = NULL, ...) {
  .resp$k <- .resp$k + 1
  r <- .resp$responses
  seen <- if (!is.null(r) && .resp$k <= length(r)) isTRUE(r[[.resp$k]]) else (stim$db <= .resp$tt)
  list(err = NULL, seen = seen, time = 0)
}
makeStim <- function(db, n) list(db = db)

run_one <- function(s) {
  .resp$k <- 0; .resp$responses <- s$Responses; .resp$tt <- s$TrueThreshold
  if (s$Algorithm == "ZEST") {
    dom <- unlist(s$Domain); prior <- unlist(s$Prior); sd <- s$LikelihoodSd
    lik <- sapply(dom, function(tt) s$Fpr + (1 - s$Fpr - s$Fnr) * (1 - stats::pnorm(dom, tt, sd)))
    st <- ZEST.start(domain = dom, prior = prior, likelihood = lik, stopType = s$StopType, stopValue = s$StopValue,
                     minStimulus = s$MinStimulus, maxStimulus = s$MaxStimulus, makeStim = makeStim, stimChoice = s$StimChoice)
    while (!ZEST.stop(st)) st <- ZEST.step(st)$state
    list(stimuli = st$stimuli, final = ZEST.final(st), stdev = ZEST.stdev(st))
  } else if (s$Algorithm == "fourTwo") {
    st <- fourTwo.start(est = s$Est, instRange = c(s$MinStimulus, s$MaxStimulus), makeStim = makeStim)
    while (!fourTwo.stop(st)) st <- fourTwo.step(st)$state
    list(stimuli = st$stimuli, final = fourTwo.final(st), stdev = NA)
  } else {
    st <- FT.start(est = s$Est, instRange = c(s$MinStimulus, s$MaxStimulus), makeStim = makeStim)
    while (!FT.stop(st)) st <- FT.step(st)$state
    list(stimuli = st$stimuli, final = FT.final(st), stdev = NA)
  }
}

fails <- 0; maxdiff <- 0
for (i in seq_along(scen)) {
  s <- scen[[i]]; c <- cs[[i]]
  stopifnot(identical(s$Id, c$Id))
  r <- run_one(s)
  cstim <- unlist(c$Stimuli)
  same_seq <- length(cstim) == length(r$stimuli) && all(abs(cstim - r$stimuli) < 1e-9)
  cfinal <- if (is.null(c$Final) || identical(c$Final, "NaN")) NA else as.numeric(c$Final)
  d <- if (is.na(cfinal) && is.na(r$final)) 0 else abs(cfinal - r$final)
  if (!is.na(r$stdev) && !is.null(c$Stdev)) d <- max(d, abs(as.numeric(c$Stdev) - r$stdev))
  maxdiff <- max(maxdiff, d, na.rm = TRUE)
  if (!same_seq || is.na(d) || d > 1e-9) {
    fails <- fails + 1
    cat(sprintf("MISMATCH %s: R stimuli %s final %s | C# stimuli %s final %s\n", s$Id,
                paste(r$stimuli, collapse = " "), r$final, paste(cstim, collapse = " "), cfinal))
  }
}
cat(sprintf("%d scenarios, %d mismatches, max |difference| in final/stdev = %.3g\n", length(scen), fails, maxdiff))
quit(status = if (fails == 0) 0 else 1)
