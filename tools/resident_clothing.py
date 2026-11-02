"""Metric rest folds for the exported work trousers (no Blender dependency).

Seams and support folds belong to the cloth, independently of the body macro.
The buried knee opening and waistband retain their exact fitted boundaries.
"""
import math


def trouser_fold(x, y, z, half):
    def smooth(a, b, v):
        t = min(1., max(0., (v-a)/(b-a)))
        return t*t*(3-2*t)

    def bell(v, width):
        return math.exp(-(v/width)**2)

    dx = abs(x)-half
    radius = math.hypot(dx, z)
    if radius < 1e-9:
        return 0.
    angle = math.atan2(z, dx)
    front = max(0., -z/radius)**2
    # Exclude the converging inseam; keep folds on each independent leg.
    leg = smooth(.025, .075, abs(x))
    opening = smooth(.415, .455, y)*(1-smooth(.81, .90, y))
    seam = .0018*bell(angle, .075)-.0007*bell(abs(angle)-.12, .055)
    hip = .0045*bell(y-(.714-.30*dx), .017)-.0020*bell(y-(.688-.30*dx), .012)
    knee = (.0036*bell(y-(.488+.27*dx), .014)
            -.0018*bell(y-(.468+.27*dx), .010)
            +.0021*bell(y-(.529-.18*dx), .018))
    return max(-.004, min(.006, (seam+(hip+knee)*front)*opening*leg))
