// Flat-lay "enclosure kit" product shot: base, lid with inlays and the mounting hardware on a table.
//   openscad -D 'part="none"' -D 'kpart="bolts"' -o build_kit/bolts.stl kit_scene.scad   (see render_kit.sh)
include <render_scene.scad>
rpart = "none";
kpart = "all";
Z0 = -75;                                  // table top in scene coordinates (matches the Blender floor)
module on_table(x, y, a = 0) translate([x, y, Z0]) rotate([0, 0, a]) children();
if (kpart == "base" || kpart == "all") on_table(-44, 14, 8) translate([-outer_l / 2, -outer_w / 2, 0]) base();
module lid_place() on_table(40, 20, -6) translate([-outer_l / 2, -outer_w / 2, 0]) children();
if (kpart == "lid" || kpart == "all") lid_place() lid();
for (c = ["green", "orange", "white"]) if (kpart == str("inlay_", c) || kpart == "all") lid_place() inlay(c);
// hardware row in front
if (kpart == "bolts" || kpart == "all") for (i = [0, 1]) on_table(-66 + i * 30, -40 + i * 4, 10 + i * 6) translate([0, 0, 5]) rotate([0, 90, 0]) din912_m6(16);
if (kpart == "washers" || kpart == "all") for (i = [0, 1]) on_table(-10 + i * 15, -40 + i * 6) din125_m6();
if (kpart == "tnuts" || kpart == "all") for (i = [0, 1]) on_table(22 + i * 17, -38 - i * 2, -15 + i * 25) rotate([180, 0, 0]) translate([0, 0, -6.6]) tnut_slot8();
if (kpart == "lidscrews" || kpart == "all") for (i = [0 : 3]) on_table(56 + (i % 2) * 9, -44 + floor(i / 2) * 9) iso7380_m3();
