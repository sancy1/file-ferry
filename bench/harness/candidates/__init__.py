# filepath: bench/harness/candidates/__init__.py
# purpose:  Candidate registry. Each candidate implements base.Candidate.
#
# Candidate names match main.py CANDIDATE_CHOICES. Import is lazy per candidate
# so a missing dependency for one does not prevent another from loading.