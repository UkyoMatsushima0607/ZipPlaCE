using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ZipPla
{
    public static partial class LongVectorImageResizer
    {
        // このファイルは生成したコード。手で直さない。
        // 線形の値（18/13/14 ビット）を 8 ビットのガンマ 2.2 の値に戻す二分探索の if 木で、
        // LongVectorImageResizer.CreateFromLinear（DEBUG ビルドのみ）の出力をそのまま貼り付けたもの。
        // 再生成の例（以前のメモより）:
        //   CreateFromLinear(20 - 13, "fromLinearIn13Out20_gamma2_2", toLinear13_gamma2_2, 1UL << 19)
        //   CreateFromLinear(21 - 14, "fromLinearIn14Out21_gamma2_2", toLinear14_gamma2_2, 1UL << 20)
        //   fromLinearIn18Out31_gamma2_2 は 31 - 18 ビットの差で生成した（offset の記録はない）
        // 生成後は gammaTableTest（DEBUG）で単調性を確かめる。
        private static byte fromLinearIn18Out31_gamma2_2(ulong linear)
        {
            if (linear <= 1541105410)
                if (linear <= 1174577076)
                    if (linear <= 1095304500)
                        if (linear <= 1078268425)
                            if (linear <= 1074654983)
                                if (linear <= 1073909565)
                                    if (linear <= 1073765195)
                                        if (linear <= 1073741824)
                                            return 0;
                                        else
                                            return 1;
                                    else
                                        if (linear <= 1073820078)
                                        return 2;
                                    else
                                        return 3;
                                else
                                    if (linear <= 1074201417)
                                    if (linear <= 1074036044)
                                        return 4;
                                    else
                                        return 5;
                                else
                                        if (linear <= 1074407272)
                                    return 6;
                                else
                                    return 7;
                            else
                                if (linear <= 1076086977)
                                if (linear <= 1075280710)
                                    if (linear <= 1074945766)
                                        return 8;
                                    else
                                        return 9;
                                else
                                    if (linear <= 1075660810)
                                    return 10;
                                else
                                    return 11;
                            else
                                    if (linear <= 1077080839)
                                if (linear <= 1076560057)
                                    return 12;
                                else
                                    return 13;
                            else
                                        if (linear <= 1077650062)
                                return 14;
                            else
                                return 15;
                        else
                            if (linear <= 1085057299)
                            if (linear <= 1081245979)
                                if (linear <= 1079655171)
                                    if (linear <= 1078936585)
                                        return 16;
                                    else
                                        return 17;
                                else
                                    if (linear <= 1080424779)
                                    return 18;
                                else
                                    return 19;
                            else
                                if (linear <= 1083045316)
                                if (linear <= 1082119316)
                                    return 20;
                                else
                                    return 21;
                            else
                                    if (linear <= 1084024482)
                                return 22;
                            else
                                return 23;
                        else
                                if (linear <= 1089734229)
                            if (linear <= 1087285748)
                                if (linear <= 1086144237)
                                    return 24;
                                else
                                    return 25;
                            else
                                if (linear <= 1088482270)
                                return 26;
                            else
                                return 27;
                        else
                                    if (linear <= 1092406093)
                            if (linear <= 1091042037)
                                return 28;
                            else
                                return 29;
                        else
                                        if (linear <= 1093826788)
                            return 30;
                        else
                            return 31;
                    else
                        if (linear <= 1126978731)
                        if (linear <= 1109221273)
                            if (linear <= 1101792764)
                                if (linear <= 1098432442)
                                    if (linear <= 1096839598)
                                        return 32;
                                    else
                                        return 33;
                                else
                                    if (linear <= 1100083383)
                                    return 34;
                                else
                                    return 35;
                            else
                                if (linear <= 1105388177)
                                if (linear <= 1103560919)
                                    return 36;
                                else
                                    return 37;
                            else
                                    if (linear <= 1107274857)
                                return 38;
                            else
                                return 39;
                        else
                            if (linear <= 1117610352)
                            if (linear <= 1113294537)
                                if (linear <= 1111227733)
                                    return 40;
                                else
                                    return 41;
                            else
                                if (linear <= 1115421980)
                                return 42;
                            else
                                return 43;
                        else
                                if (linear <= 1122171013)
                            if (linear <= 1119859936)
                                return 44;
                            else
                                return 45;
                        else
                                    if (linear <= 1124543854)
                            return 46;
                        else
                            return 47;
                    else
                            if (linear <= 1148721852)
                        if (linear <= 1137343811)
                            if (linear <= 1132035643)
                                if (linear <= 1129475907)
                                    return 48;
                                else
                                    return 49;
                            else
                                if (linear <= 1134658193)
                                return 50;
                            else
                                return 51;
                        else
                            if (linear <= 1142905235)
                            if (linear <= 1140092744)
                                return 52;
                            else
                                return 53;
                        else
                                if (linear <= 1145781526)
                            return 54;
                        else
                            return 55;
                    else
                                if (linear <= 1161128134)
                        if (linear <= 1154795543)
                            if (linear <= 1151726448)
                                return 56;
                            else
                                return 57;
                        else
                            if (linear <= 1157929364)
                            return 58;
                        else
                            return 59;
                    else
                                    if (linear <= 1167721402)
                        if (linear <= 1164392074)
                            return 60;
                        else
                            return 61;
                    else
                                        if (linear <= 1171116332)
                        return 62;
                    else
                        return 63;
                else
                    if (linear <= 1321218420)
                    if (linear <= 1239063043)
                        if (linear <= 1204656924)
                            if (linear <= 1189082342)
                                if (linear <= 1181696842)
                                    if (linear <= 1178103844)
                                        return 64;
                                    else
                                        return 65;
                                else
                                    if (linear <= 1185356274)
                                    return 66;
                                else
                                    return 67;
                            else
                                if (linear <= 1196735181)
                                if (linear <= 1192875246)
                                    return 68;
                                else
                                    return 69;
                            else
                                    if (linear <= 1200662343)
                                return 70;
                            else
                                return 71;
                        else
                            if (linear <= 1221313209)
                            if (linear <= 1212849101)
                                if (linear <= 1208719114)
                                    return 72;
                                else
                                    return 73;
                            else
                                if (linear <= 1217047071)
                                return 74;
                            else
                                return 75;
                        else
                                if (linear <= 1230050712)
                            if (linear <= 1225647696)
                                return 76;
                            else
                                return 77;
                        else
                                    if (linear <= 1234522435)
                            return 78;
                        else
                            return 79;
                    else
                        if (linear <= 1277888320)
                        if (linear <= 1257917778)
                            if (linear <= 1248351606)
                                if (linear <= 1243672709)
                                    return 80;
                                else
                                    return 81;
                            else
                                if (linear <= 1253099906)
                                return 82;
                            else
                                return 83;
                        else
                            if (linear <= 1267762908)
                            if (linear <= 1262805390)
                                return 84;
                            else
                                return 85;
                        else
                                if (linear <= 1272790497)
                            return 86;
                        else
                            return 87;
                    else
                            if (linear <= 1298985161)
                        if (linear <= 1288295312)
                            if (linear <= 1283056538)
                                return 88;
                            else
                                return 89;
                        else
                            if (linear <= 1293604801)
                            return 90;
                        else
                            return 91;
                    else
                                if (linear <= 1309959120)
                        if (linear <= 1304436550)
                            return 92;
                        else
                            return 93;
                    else
                                    if (linear <= 1315553026)
                        return 94;
                    else
                        return 95;
                else
                        if (linear <= 1421706304)
                    if (linear <= 1369132942)
                        if (linear <= 1344597865)
                            if (linear <= 1332764272)
                                if (linear <= 1326955452)
                                    return 96;
                                else
                                    return 97;
                            else
                                if (linear <= 1338645027)
                                return 98;
                            else
                                return 99;
                        else
                            if (linear <= 1356720371)
                            if (linear <= 1350622931)
                                return 100;
                            else
                                return 101;
                        else
                                if (linear <= 1362890327)
                            return 102;
                        else
                            return 103;
                    else
                        if (linear <= 1394832799)
                        if (linear <= 1381836712)
                            if (linear <= 1375448357)
                                return 104;
                            else
                                return 105;
                        else
                            if (linear <= 1388298147)
                            return 106;
                        else
                            return 107;
                    else
                            if (linear <= 1408122301)
                        if (linear <= 1401440805)
                            return 108;
                        else
                            return 109;
                    else
                                if (linear <= 1414877423)
                        return 110;
                    else
                        return 111;
                else
                            if (linear <= 1479008449)
                    if (linear <= 1449762066)
                        if (linear <= 1435585874)
                            if (linear <= 1428609077)
                                return 112;
                            else
                                return 113;
                        else
                            if (linear <= 1442636827)
                            return 114;
                        else
                            return 115;
                    else
                        if (linear <= 1464235916)
                        if (linear <= 1456961719)
                            return 116;
                        else
                            return 117;
                    else
                            if (linear <= 1471584784)
                        return 118;
                    else
                        return 119;
                else
                                if (linear <= 1509453590)
                    if (linear <= 1494080675)
                        if (linear <= 1486507038)
                            return 120;
                        else
                            return 121;
                    else
                        if (linear <= 1501729485)
                        return 122;
                    else
                        return 123;
                else
                                    if (linear <= 1525128178)
                    if (linear <= 1517253114)
                        return 124;
                    else
                        return 125;
                else
                                        if (linear <= 1533078903)
                    return 126;
                else
                    return 127;
            else
                if (linear <= 2217430622)
                if (linear <= 1838648814)
                    if (linear <= 1679931024)
                        if (linear <= 1608059764)
                            if (linear <= 1573971625)
                                if (linear <= 1557386243)
                                    if (linear <= 1549207817)
                                        return 128;
                                    else
                                        return 129;
                                else
                                    if (linear <= 1565640807)
                                    return 130;
                                else
                                    return 131;
                            else
                                if (linear <= 1590862491)
                                if (linear <= 1582378815)
                                    return 132;
                                else
                                    return 133;
                            else
                                    if (linear <= 1599422770)
                                return 134;
                            else
                                return 135;
                        else
                            if (linear <= 1643377173)
                            if (linear <= 1625564358)
                                if (linear <= 1616773589)
                                    return 136;
                                else
                                    return 137;
                            else
                                if (linear <= 1634432181)
                                return 138;
                            else
                                return 139;
                        else
                                if (linear <= 1661499101)
                            if (linear <= 1652399442)
                                return 140;
                            else
                                return 141;
                        else
                                    if (linear <= 1670676258)
                            return 142;
                        else
                            return 143;
                    else
                        if (linear <= 1756775947)
                        if (linear <= 1717728330)
                            if (linear <= 1698673813)
                                if (linear <= 1689263506)
                                    return 144;
                                else
                                    return 145;
                            else
                                if (linear <= 1708162052)
                                return 146;
                            else
                                return 147;
                        else
                            if (linear <= 1737095427)
                            if (linear <= 1727372753)
                                return 148;
                            else
                                return 149;
                        else
                                if (linear <= 1746896457)
                            return 150;
                        else
                            return 151;
                    else
                            if (linear <= 1797080585)
                        if (linear <= 1776770725)
                            if (linear <= 1766734002)
                                return 152;
                            else
                                return 153;
                        else
                            if (linear <= 1786886218)
                            return 154;
                        else
                            return 155;
                    else
                                if (linear <= 1817706346)
                        if (linear <= 1807353927)
                            return 156;
                        else
                            return 157;
                    else
                                    if (linear <= 1828137941)
                        return 158;
                    else
                        return 159;
                else
                    if (linear <= 2017684454)
                    if (linear <= 1925601656)
                        if (linear <= 1881487068)
                            if (linear <= 1859908791)
                                if (linear <= 1849239064)
                                    return 160;
                                else
                                    return 161;
                            else
                                if (linear <= 1870658093)
                                return 162;
                            else
                                return 163;
                        else
                            if (linear <= 1903384431)
                            if (linear <= 1892395815)
                                return 164;
                            else
                                return 165;
                        else
                                if (linear <= 1914453012)
                            return 166;
                        else
                            return 167;
                    else
                        if (linear <= 1970998762)
                        if (linear <= 1948139512)
                            if (linear <= 1936830457)
                                return 168;
                            else
                                return 169;
                        else
                            if (linear <= 1959528915)
                            return 170;
                        else
                            return 171;
                    else
                            if (linear <= 1994180160)
                        if (linear <= 1982549145)
                            return 172;
                        else
                            return 173;
                    else
                                if (linear <= 2005891898)
                        return 174;
                    else
                        return 175;
                else
                        if (linear <= 2114945316)
                    if (linear <= 2065664690)
                        if (linear <= 2041512386)
                            if (linear <= 2029557919)
                                return 176;
                            else
                                return 177;
                        else
                            if (linear <= 2053547946)
                            return 178;
                        else
                            return 179;
                    else
                        if (linear <= 2090142092)
                        if (linear <= 2077862708)
                            return 180;
                        else
                            return 181;
                    else
                            if (linear <= 2102502932)
                        return 182;
                    else
                        return 183;
                else
                            if (linear <= 2165532078)
                    if (linear <= 2140075075)
                        if (linear <= 2127469334)
                            return 184;
                        else
                            return 185;
                    else
                        if (linear <= 2152762627)
                        return 186;
                    else
                        return 187;
                else
                                if (linear <= 2191317028)
                    if (linear <= 2178383516)
                        return 188;
                    else
                        return 189;
                else
                                    if (linear <= 2204332702)
                    return 190;
                else
                    return 191;
            else
                    if (linear <= 2680490614)
                if (linear <= 2438252256)
                    if (linear <= 2325185163)
                        if (linear <= 2270646498)
                            if (linear <= 2243873550)
                                if (linear <= 2230610877)
                                    return 192;
                                else
                                    return 193;
                            else
                                if (linear <= 2257218729)
                                return 194;
                            else
                                return 195;
                        else
                            if (linear <= 2297750144)
                            if (linear <= 2284156942)
                                return 196;
                            else
                                return 197;
                        else
                                if (linear <= 2311426190)
                            return 198;
                        else
                            return 199;
                    else
                        if (linear <= 2381051988)
                        if (linear <= 2352952224)
                            if (linear <= 2339027147)
                                return 200;
                            else
                                return 201;
                        else
                            if (linear <= 2366960477)
                            return 202;
                        else
                            return 203;
                    else
                            if (linear <= 2409485115)
                        if (linear <= 2395226840)
                            return 204;
                        else
                            return 205;
                    else
                                if (linear <= 2423826893)
                        return 206;
                    else
                        return 207;
                else
                    if (linear <= 2556673849)
                    if (linear <= 2496791169)
                        if (linear <= 2467354060)
                            if (linear <= 2452761285)
                                return 208;
                            else
                                return 209;
                        else
                            if (linear <= 2482030662)
                            return 210;
                        else
                            return 211;
                    else
                        if (linear <= 2526564221)
                        if (linear <= 2511635663)
                            return 212;
                        else
                            return 213;
                    else
                            if (linear <= 2541576923)
                        return 214;
                    else
                        return 215;
                else
                        if (linear <= 2617905340)
                    if (linear <= 2587120681)
                        if (linear <= 2571855075)
                            return 216;
                        else
                            return 217;
                    else
                        if (linear <= 2602470743)
                        return 218;
                    else
                        return 219;
                else
                            if (linear <= 2649028446)
                    if (linear <= 2633424549)
                        return 220;
                    else
                        return 221;
                else
                                if (linear <= 2664717109)
                    return 222;
                else
                    return 223;
            else
                        if (linear <= 2944466474)
                if (linear <= 2809742033)
                    if (linear <= 2744434568)
                        if (linear <= 2712292453)
                            if (linear <= 2696349036)
                                return 224;
                            else
                                return 225;
                        else
                            if (linear <= 2728320938)
                            return 226;
                        else
                            return 227;
                    else
                        if (linear <= 2776917563)
                        if (linear <= 2760633418)
                            return 228;
                        else
                            return 229;
                    else
                            if (linear <= 2793287076)
                        return 230;
                    else
                        return 231;
                else
                    if (linear <= 2876417770)
                    if (linear <= 2842908572)
                        if (linear <= 2826282507)
                            return 232;
                        else
                            return 233;
                    else
                        if (linear <= 2859620302)
                        return 234;
                    else
                        return 235;
                else
                        if (linear <= 2910270210)
                    if (linear <= 2893301048)
                        return 236;
                    else
                        return 237;
                else
                            if (linear <= 2927325328)
                    return 238;
                else
                    return 239;
            else
                            if (linear <= 3084701256)
                if (linear <= 3013892779)
                    if (linear <= 2979007139)
                        if (linear <= 2961693721)
                            return 240;
                        else
                            return 241;
                    else
                        if (linear <= 2996406802)
                        return 242;
                    else
                        return 243;
                else
                    if (linear <= 3049123962)
                    if (linear <= 3031465142)
                        return 244;
                    else
                        return 245;
                else
                        if (linear <= 3066869310)
                    return 246;
                else
                    return 247;
            else
                                if (linear <= 3156896416)
                if (linear <= 3120625221)
                    if (linear <= 3102619869)
                        return 248;
                    else
                        return 249;
                else
                    if (linear <= 3138717380)
                    return 250;
                else
                    return 251;
            else
                                    if (linear <= 3193515397)
                if (linear <= 3175162399)
                    return 252;
                else
                    return 253;
            else
                                        if (linear <= 3211955479)
                return 254;
            else
                return 255;
        }

        private static byte fromLinearIn13Out20_gamma2_2(ulong linear)
        {
            if (linear <= 752867)
                if (linear <= 574007)
                    if (linear <= 535323)
                        if (linear <= 527009)
                            if (linear <= 525246)
                                if (linear <= 524731)
                                    if (linear <= 524469)
                                        if (linear <= 524288)
                                            return 0;
                                        else
                                            return 1;
                                    else
                                        if (linear <= 524602)
                                        return 2;
                                    else
                                        return 3;
                                else
                                    if (linear <= 524989)
                                    if (linear <= 524860)
                                        return 4;
                                    else
                                        return 5;
                                else
                                        if (linear <= 525118)
                                    return 6;
                                else
                                    return 7;
                            else
                                if (linear <= 525945)
                                if (linear <= 525551)
                                    if (linear <= 525388)
                                        return 8;
                                    else
                                        return 9;
                                else
                                    if (linear <= 525737)
                                    return 10;
                                else
                                    return 11;
                            else
                                    if (linear <= 526430)
                                if (linear <= 526176)
                                    return 12;
                                else
                                    return 13;
                            else
                                        if (linear <= 526708)
                                return 14;
                            else
                                return 15;
                        else
                            if (linear <= 530322)
                            if (linear <= 528462)
                                if (linear <= 527686)
                                    if (linear <= 527335)
                                        return 16;
                                    else
                                        return 17;
                                else
                                    if (linear <= 528062)
                                    return 18;
                                else
                                    return 19;
                            else
                                if (linear <= 529340)
                                if (linear <= 528889)
                                    return 20;
                                else
                                    return 21;
                            else
                                    if (linear <= 529818)
                                return 22;
                            else
                                return 23;
                        else
                                if (linear <= 532605)
                            if (linear <= 531410)
                                if (linear <= 530853)
                                    return 24;
                                else
                                    return 25;
                            else
                                if (linear <= 531994)
                                return 26;
                            else
                                return 27;
                        else
                                    if (linear <= 533908)
                            if (linear <= 533243)
                                return 28;
                            else
                                return 29;
                        else
                                        if (linear <= 534602)
                            return 30;
                        else
                            return 31;
                    else
                        if (linear <= 550779)
                        if (linear <= 542114)
                            if (linear <= 538489)
                                if (linear <= 536849)
                                    if (linear <= 536072)
                                        return 32;
                                    else
                                        return 33;
                                else
                                    if (linear <= 537655)
                                    return 34;
                                else
                                    return 35;
                            else
                                if (linear <= 540243)
                                if (linear <= 539352)
                                    return 36;
                                else
                                    return 37;
                            else
                                    if (linear <= 541164)
                                return 38;
                            else
                                return 39;
                        else
                            if (linear <= 546208)
                            if (linear <= 544102)
                                if (linear <= 543093)
                                    return 40;
                                else
                                    return 41;
                            else
                                if (linear <= 545140)
                                return 42;
                            else
                                return 43;
                        else
                                if (linear <= 548433)
                            if (linear <= 547305)
                                return 44;
                            else
                                return 45;
                        else
                                    if (linear <= 549591)
                            return 46;
                        else
                            return 47;
                    else
                            if (linear <= 561390)
                        if (linear <= 555837)
                            if (linear <= 553247)
                                if (linear <= 551998)
                                    return 48;
                                else
                                    return 49;
                            else
                                if (linear <= 554527)
                                return 50;
                            else
                                return 51;
                        else
                            if (linear <= 558551)
                            if (linear <= 557179)
                                return 52;
                            else
                                return 53;
                        else
                                if (linear <= 559955)
                            return 54;
                        else
                            return 55;
                    else
                                if (linear <= 567444)
                        if (linear <= 564353)
                            if (linear <= 562856)
                                return 56;
                            else
                                return 57;
                        else
                            if (linear <= 565883)
                            return 58;
                        else
                            return 59;
                    else
                                    if (linear <= 570661)
                        if (linear <= 569036)
                            return 60;
                        else
                            return 61;
                    else
                                        if (linear <= 572318)
                        return 62;
                    else
                        return 63;
                else
                    if (linear <= 645565)
                    if (linear <= 605475)
                        if (linear <= 588685)
                            if (linear <= 581085)
                                if (linear <= 577481)
                                    if (linear <= 575728)
                                        return 64;
                                    else
                                        return 65;
                                else
                                    if (linear <= 579267)
                                    return 66;
                                else
                                    return 67;
                            else
                                if (linear <= 584819)
                                if (linear <= 582936)
                                    return 68;
                                else
                                    return 69;
                            else
                                    if (linear <= 586736)
                                return 70;
                            else
                                return 71;
                        else
                            if (linear <= 596813)
                            if (linear <= 592683)
                                if (linear <= 590667)
                                    return 72;
                                else
                                    return 73;
                            else
                                if (linear <= 594731)
                                return 74;
                            else
                                return 75;
                        else
                                if (linear <= 601077)
                            if (linear <= 598928)
                                return 76;
                            else
                                return 77;
                        else
                                    if (linear <= 603259)
                            return 78;
                        else
                            return 79;
                    else
                        if (linear <= 624421)
                        if (linear <= 614676)
                            if (linear <= 610007)
                                if (linear <= 607724)
                                    return 80;
                                else
                                    return 81;
                            else
                                if (linear <= 612325)
                                return 82;
                            else
                                return 83;
                        else
                            if (linear <= 619480)
                            if (linear <= 617061)
                                return 84;
                            else
                                return 85;
                        else
                                if (linear <= 621933)
                            return 86;
                        else
                            return 87;
                    else
                            if (linear <= 634716)
                        if (linear <= 629499)
                            if (linear <= 626943)
                                return 88;
                            else
                                return 89;
                        else
                            if (linear <= 632090)
                            return 90;
                        else
                            return 91;
                    else
                                if (linear <= 640071)
                        if (linear <= 637376)
                            return 92;
                        else
                            return 93;
                    else
                                    if (linear <= 642801)
                        return 94;
                    else
                        return 95;
                else
                        if (linear <= 694602)
                    if (linear <= 668947)
                        if (linear <= 656974)
                            if (linear <= 651200)
                                if (linear <= 648365)
                                    return 96;
                                else
                                    return 97;
                            else
                                if (linear <= 654069)
                                return 98;
                            else
                                return 99;
                        else
                            if (linear <= 662890)
                            if (linear <= 659914)
                                return 100;
                            else
                                return 101;
                        else
                                if (linear <= 665901)
                            return 102;
                        else
                            return 103;
                    else
                        if (linear <= 681488)
                        if (linear <= 675146)
                            if (linear <= 672029)
                                return 104;
                            else
                                return 105;
                        else
                            if (linear <= 678299)
                            return 106;
                        else
                            return 107;
                    else
                            if (linear <= 687973)
                        if (linear <= 684713)
                            return 108;
                        else
                            return 109;
                    else
                                if (linear <= 691269)
                        return 110;
                    else
                        return 111;
                else
                            if (linear <= 722564)
                    if (linear <= 708293)
                        if (linear <= 701375)
                            if (linear <= 697970)
                                return 112;
                            else
                                return 113;
                        else
                            if (linear <= 704816)
                            return 114;
                        else
                            return 115;
                    else
                        if (linear <= 715356)
                        if (linear <= 711806)
                            return 116;
                        else
                            return 117;
                    else
                            if (linear <= 718942)
                        return 118;
                    else
                        return 119;
                else
                                if (linear <= 737421)
                    if (linear <= 729919)
                        if (linear <= 726224)
                            return 120;
                        else
                            return 121;
                    else
                        if (linear <= 733652)
                        return 122;
                    else
                        return 123;
                else
                                    if (linear <= 745070)
                    if (linear <= 741227)
                        return 124;
                    else
                        return 125;
                else
                                        if (linear <= 748950)
                    return 126;
                else
                    return 127;
            else
                if (linear <= 1082903)
                if (linear <= 898064)
                    if (linear <= 820612)
                        if (linear <= 785540)
                            if (linear <= 768905)
                                if (linear <= 760812)
                                    if (linear <= 756821)
                                        return 128;
                                    else
                                        return 129;
                                else
                                    if (linear <= 764840)
                                    return 130;
                                else
                                    return 131;
                            else
                                if (linear <= 777148)
                                if (linear <= 773008)
                                    return 132;
                                else
                                    return 133;
                            else
                                    if (linear <= 781325)
                                return 134;
                            else
                                return 135;
                        else
                            if (linear <= 802774)
                            if (linear <= 794082)
                                if (linear <= 789792)
                                    return 136;
                                else
                                    return 137;
                            else
                                if (linear <= 798409)
                                return 138;
                            else
                                return 139;
                        else
                                if (linear <= 811617)
                            if (linear <= 807177)
                                return 140;
                            else
                                return 141;
                        else
                                    if (linear <= 816095)
                            return 142;
                        else
                            return 143;
                    else
                        if (linear <= 858111)
                        if (linear <= 839056)
                            if (linear <= 829758)
                                if (linear <= 825166)
                                    return 144;
                                else
                                    return 145;
                            else
                                if (linear <= 834388)
                                return 146;
                            else
                                return 147;
                        else
                            if (linear <= 848507)
                            if (linear <= 843762)
                                return 148;
                            else
                                return 149;
                        else
                                if (linear <= 853290)
                            return 150;
                        else
                            return 151;
                    else
                            if (linear <= 877779)
                        if (linear <= 867868)
                            if (linear <= 862970)
                                return 152;
                            else
                                return 153;
                        else
                            if (linear <= 872804)
                            return 154;
                        else
                            return 155;
                    else
                                if (linear <= 887844)
                        if (linear <= 882792)
                            return 156;
                        else
                            return 157;
                    else
                                    if (linear <= 892934)
                        return 158;
                    else
                        return 159;
                else
                    if (linear <= 985430)
                    if (linear <= 940495)
                        if (linear <= 918968)
                            if (linear <= 908438)
                                if (linear <= 903231)
                                    return 160;
                                else
                                    return 161;
                            else
                                if (linear <= 913684)
                                return 162;
                            else
                                return 163;
                        else
                            if (linear <= 929654)
                            if (linear <= 924291)
                                return 164;
                            else
                                return 165;
                        else
                                if (linear <= 935055)
                            return 166;
                        else
                            return 167;
                    else
                        if (linear <= 962648)
                        if (linear <= 951493)
                            if (linear <= 945975)
                                return 168;
                            else
                                return 169;
                        else
                            if (linear <= 957051)
                            return 170;
                        else
                            return 171;
                    else
                            if (linear <= 973960)
                        if (linear <= 968285)
                            return 172;
                        else
                            return 173;
                    else
                                if (linear <= 979676)
                        return 174;
                    else
                        return 175;
                else
                        if (linear <= 1032892)
                    if (linear <= 1008844)
                        if (linear <= 997058)
                            if (linear <= 991224)
                                return 176;
                            else
                                return 177;
                        else
                            if (linear <= 1002931)
                            return 178;
                        else
                            return 179;
                    else
                        if (linear <= 1020788)
                        if (linear <= 1014796)
                            return 180;
                        else
                            return 181;
                    else
                            if (linear <= 1026820)
                        return 182;
                    else
                        return 183;
                else
                            if (linear <= 1057578)
                    if (linear <= 1045155)
                        if (linear <= 1039004)
                            return 184;
                        else
                            return 185;
                    else
                        if (linear <= 1051346)
                        return 186;
                    else
                        return 187;
                else
                                if (linear <= 1070160)
                    if (linear <= 1063849)
                        return 188;
                    else
                        return 189;
                else
                                    if (linear <= 1076512)
                    return 190;
                else
                    return 191;
            else
                    if (linear <= 1308870)
                if (linear <= 1190661)
                    if (linear <= 1135486)
                        if (linear <= 1108872)
                            if (linear <= 1095807)
                                if (linear <= 1089335)
                                    return 192;
                                else
                                    return 193;
                            else
                                if (linear <= 1102319)
                                return 194;
                            else
                                return 195;
                        else
                            if (linear <= 1122098)
                            if (linear <= 1115465)
                                return 196;
                            else
                                return 197;
                        else
                                if (linear <= 1128772)
                            return 198;
                        else
                            return 199;
                    else
                        if (linear <= 1162748)
                        if (linear <= 1149036)
                            if (linear <= 1142241)
                                return 200;
                            else
                                return 201;
                        else
                            if (linear <= 1155872)
                            return 202;
                        else
                            return 203;
                    else
                            if (linear <= 1176623)
                        if (linear <= 1169665)
                            return 204;
                        else
                            return 205;
                    else
                                if (linear <= 1183622)
                        return 206;
                    else
                        return 207;
                else
                    if (linear <= 1248449)
                    if (linear <= 1219227)
                        if (linear <= 1204862)
                            if (linear <= 1197741)
                                return 208;
                            else
                                return 209;
                        else
                            if (linear <= 1212024)
                            return 210;
                        else
                            return 211;
                    else
                        if (linear <= 1233756)
                        if (linear <= 1226471)
                            return 212;
                        else
                            return 213;
                    else
                            if (linear <= 1241082)
                        return 214;
                    else
                        return 215;
                else
                        if (linear <= 1278329)
                    if (linear <= 1263306)
                        if (linear <= 1255857)
                            return 216;
                        else
                            return 217;
                    else
                        if (linear <= 1270797)
                        return 218;
                    else
                        return 219;
                else
                            if (linear <= 1293517)
                    if (linear <= 1285902)
                        return 220;
                    else
                        return 221;
                else
                                if (linear <= 1301172)
                    return 222;
                else
                    return 223;
            else
                        if (linear <= 1437686)
                if (linear <= 1371942)
                    if (linear <= 1340073)
                        if (linear <= 1324388)
                            if (linear <= 1316608)
                                return 224;
                            else
                                return 225;
                        else
                            if (linear <= 1332210)
                            return 226;
                        else
                            return 227;
                    else
                        if (linear <= 1355924)
                        if (linear <= 1347978)
                            return 228;
                        else
                            return 229;
                    else
                            if (linear <= 1363913)
                        return 230;
                    else
                        return 231;
                else
                    if (linear <= 1404479)
                    if (linear <= 1388127)
                        if (linear <= 1380014)
                            return 232;
                        else
                            return 233;
                    else
                        if (linear <= 1396282)
                        return 234;
                    else
                        return 235;
                else
                        if (linear <= 1420999)
                    if (linear <= 1412718)
                        return 236;
                    else
                        return 237;
                else
                            if (linear <= 1429321)
                    return 238;
                else
                    return 239;
            else
                            if (linear <= 1506118)
                if (linear <= 1471565)
                    if (linear <= 1454541)
                        if (linear <= 1446092)
                            return 240;
                        else
                            return 241;
                    else
                        if (linear <= 1463032)
                        return 242;
                    else
                        return 243;
                else
                    if (linear <= 1488757)
                    if (linear <= 1480140)
                        return 244;
                    else
                        return 245;
                else
                        if (linear <= 1497417)
                    return 246;
                else
                    return 247;
            else
                                if (linear <= 1541348)
                if (linear <= 1523649)
                    if (linear <= 1514862)
                        return 248;
                    else
                        return 249;
                else
                    if (linear <= 1532477)
                    return 250;
                else
                    return 251;
            else
                                    if (linear <= 1559218)
                if (linear <= 1550262)
                    return 252;
                else
                    return 253;
            else
                                        if (linear <= 1568216)
                return 254;
            else
                return 255;
        }

        private static byte fromLinearIn14Out21_gamma2_2(ulong linear)
        {
            if (linear <= 1505185)
                if (linear <= 1147316)
                    if (linear <= 1069917)
                        if (linear <= 1053283)
                            if (linear <= 1049755)
                                if (linear <= 1049019)
                                    if (linear <= 1048757)
                                        if (linear <= 1048576)
                                            return 0;
                                        else
                                            return 1;
                                    else
                                        if (linear <= 1048890)
                                        return 2;
                                    else
                                        return 3;
                                else
                                    if (linear <= 1049312)
                                    if (linear <= 1049151)
                                        return 4;
                                    else
                                        return 5;
                                else
                                        if (linear <= 1049513)
                                    return 6;
                                else
                                    return 7;
                            else
                                if (linear <= 1051153)
                                if (linear <= 1050366)
                                    if (linear <= 1050039)
                                        return 8;
                                    else
                                        return 9;
                                else
                                    if (linear <= 1050737)
                                    return 10;
                                else
                                    return 11;
                            else
                                    if (linear <= 1052124)
                                if (linear <= 1051615)
                                    return 12;
                                else
                                    return 13;
                            else
                                        if (linear <= 1052679)
                                return 14;
                            else
                                return 15;
                        else
                            if (linear <= 1059912)
                            if (linear <= 1056190)
                                if (linear <= 1054637)
                                    if (linear <= 1053936)
                                        return 16;
                                    else
                                        return 17;
                                else
                                    if (linear <= 1055389)
                                    return 18;
                                else
                                    return 19;
                            else
                                if (linear <= 1057947)
                                if (linear <= 1057043)
                                    return 20;
                                else
                                    return 21;
                            else
                                    if (linear <= 1058903)
                                return 22;
                            else
                                return 23;
                        else
                                if (linear <= 1064478)
                            if (linear <= 1062087)
                                if (linear <= 1060973)
                                    return 24;
                                else
                                    return 25;
                            else
                                if (linear <= 1063256)
                                return 26;
                            else
                                return 27;
                        else
                                    if (linear <= 1067087)
                            if (linear <= 1065755)
                                return 28;
                            else
                                return 29;
                        else
                                        if (linear <= 1068474)
                            return 30;
                        else
                            return 31;
                    else
                        if (linear <= 1100843)
                        if (linear <= 1083505)
                            if (linear <= 1076252)
                                if (linear <= 1072971)
                                    if (linear <= 1071416)
                                        return 32;
                                    else
                                        return 33;
                                else
                                    if (linear <= 1074583)
                                    return 34;
                                else
                                    return 35;
                            else
                                if (linear <= 1079762)
                                if (linear <= 1077978)
                                    return 36;
                                else
                                    return 37;
                            else
                                    if (linear <= 1081604)
                                return 38;
                            else
                                return 39;
                        else
                            if (linear <= 1091696)
                            if (linear <= 1087482)
                                if (linear <= 1085464)
                                    return 40;
                                else
                                    return 41;
                            else
                                if (linear <= 1089559)
                                return 42;
                            else
                                return 43;
                        else
                                if (linear <= 1096148)
                            if (linear <= 1093892)
                                return 44;
                            else
                                return 45;
                        else
                                    if (linear <= 1098465)
                            return 46;
                        else
                            return 47;
                    else
                            if (linear <= 1122072)
                        if (linear <= 1110963)
                            if (linear <= 1105780)
                                if (linear <= 1103281)
                                    return 48;
                                else
                                    return 49;
                            else
                                if (linear <= 1108341)
                                return 50;
                            else
                                return 51;
                        else
                            if (linear <= 1116393)
                            if (linear <= 1113647)
                                return 52;
                            else
                                return 53;
                        else
                                if (linear <= 1119201)
                            return 54;
                        else
                            return 55;
                    else
                                if (linear <= 1134185)
                        if (linear <= 1128002)
                            if (linear <= 1125006)
                                return 56;
                            else
                                return 57;
                        else
                            if (linear <= 1131062)
                            return 58;
                        else
                            return 59;
                    else
                                    if (linear <= 1140623)
                        if (linear <= 1137372)
                            return 60;
                        else
                            return 61;
                    else
                                        if (linear <= 1143937)
                        return 62;
                    else
                        return 63;
                else
                    if (linear <= 1290493)
                    if (linear <= 1210279)
                        if (linear <= 1176685)
                            if (linear <= 1161479)
                                if (linear <= 1154268)
                                    if (linear <= 1150760)
                                        return 64;
                                    else
                                        return 65;
                                else
                                    if (linear <= 1157841)
                                    return 66;
                                else
                                    return 67;
                            else
                                if (linear <= 1168951)
                                if (linear <= 1165182)
                                    return 68;
                                else
                                    return 69;
                            else
                                    if (linear <= 1172785)
                                return 70;
                            else
                                return 71;
                        else
                            if (linear <= 1192948)
                            if (linear <= 1184684)
                                if (linear <= 1180652)
                                    return 72;
                                else
                                    return 73;
                            else
                                if (linear <= 1188783)
                                return 74;
                            else
                                return 75;
                        else
                                if (linear <= 1201479)
                            if (linear <= 1197180)
                                return 76;
                            else
                                return 77;
                        else
                                    if (linear <= 1205845)
                            return 78;
                        else
                            return 79;
                    else
                        if (linear <= 1248187)
                        if (linear <= 1228688)
                            if (linear <= 1219348)
                                if (linear <= 1214779)
                                    return 80;
                                else
                                    return 81;
                            else
                                if (linear <= 1223984)
                                return 82;
                            else
                                return 83;
                        else
                            if (linear <= 1238300)
                            if (linear <= 1233460)
                                return 84;
                            else
                                return 85;
                        else
                                if (linear <= 1243209)
                            return 86;
                        else
                            return 87;
                    else
                            if (linear <= 1268785)
                        if (linear <= 1258348)
                            if (linear <= 1253233)
                                return 88;
                            else
                                return 89;
                        else
                            if (linear <= 1263532)
                            return 90;
                        else
                            return 91;
                    else
                                if (linear <= 1279500)
                        if (linear <= 1274108)
                            return 92;
                        else
                            return 93;
                    else
                                    if (linear <= 1284961)
                        return 94;
                    else
                        return 95;
                else
                        if (linear <= 1388607)
                    if (linear <= 1337275)
                        if (linear <= 1313320)
                            if (linear <= 1301766)
                                if (linear <= 1296094)
                                    return 96;
                                else
                                    return 97;
                            else
                                if (linear <= 1307508)
                                return 98;
                            else
                                return 99;
                        else
                            if (linear <= 1325156)
                            if (linear <= 1319203)
                                return 100;
                            else
                                return 101;
                        else
                                if (linear <= 1331180)
                            return 102;
                        else
                            return 103;
                    else
                        if (linear <= 1362368)
                        if (linear <= 1349679)
                            if (linear <= 1343442)
                                return 104;
                            else
                                return 105;
                        else
                            if (linear <= 1355988)
                            return 106;
                        else
                            return 107;
                    else
                            if (linear <= 1375343)
                        if (linear <= 1368820)
                            return 108;
                        else
                            return 109;
                    else
                                if (linear <= 1381939)
                        return 110;
                    else
                        return 111;
                else
                            if (linear <= 1444555)
                    if (linear <= 1415999)
                        if (linear <= 1402158)
                            if (linear <= 1395346)
                                return 112;
                            else
                                return 113;
                        else
                            if (linear <= 1409043)
                            return 114;
                        else
                            return 115;
                    else
                        if (linear <= 1430131)
                        if (linear <= 1423029)
                            return 116;
                        else
                            return 117;
                    else
                            if (linear <= 1437307)
                        return 118;
                    else
                        return 119;
                else
                                if (linear <= 1474281)
                    if (linear <= 1459271)
                        if (linear <= 1451876)
                            return 120;
                        else
                            return 121;
                    else
                        if (linear <= 1466739)
                        return 122;
                    else
                        return 123;
                else
                                    if (linear <= 1489585)
                    if (linear <= 1481896)
                        return 124;
                    else
                        return 125;
                else
                                        if (linear <= 1497348)
                    return 126;
                else
                    return 127;
            else
                if (linear <= 2165530)
                if (linear <= 1795698)
                    if (linear <= 1640730)
                        if (linear <= 1570557)
                            if (linear <= 1537274)
                                if (linear <= 1521081)
                                    if (linear <= 1513096)
                                        return 128;
                                    else
                                        return 129;
                                else
                                    if (linear <= 1529140)
                                    return 130;
                                else
                                    return 131;
                            else
                                if (linear <= 1553766)
                                if (linear <= 1545483)
                                    return 132;
                                else
                                    return 133;
                            else
                                    if (linear <= 1562124)
                                return 134;
                            else
                                return 135;
                        else
                            if (linear <= 1605040)
                            if (linear <= 1587648)
                                if (linear <= 1579065)
                                    return 136;
                                else
                                    return 137;
                            else
                                if (linear <= 1596306)
                                return 138;
                            else
                                return 139;
                        else
                                if (linear <= 1622734)
                            if (linear <= 1613849)
                                return 140;
                            else
                                return 141;
                        else
                                    if (linear <= 1631694)
                            return 142;
                        else
                            return 143;
                    else
                        if (linear <= 1715759)
                        if (linear <= 1677634)
                            if (linear <= 1659030)
                                if (linear <= 1649842)
                                    return 144;
                                else
                                    return 145;
                            else
                                if (linear <= 1668294)
                                return 146;
                            else
                                return 147;
                        else
                            if (linear <= 1696544)
                            if (linear <= 1687051)
                                return 148;
                            else
                                return 149;
                        else
                                if (linear <= 1706113)
                            return 150;
                        else
                            return 151;
                    else
                            if (linear <= 1755112)
                        if (linear <= 1735282)
                            if (linear <= 1725482)
                                return 152;
                            else
                                return 153;
                        else
                            if (linear <= 1745158)
                            return 154;
                        else
                            return 155;
                    else
                                if (linear <= 1775250)
                        if (linear <= 1765142)
                            return 156;
                        else
                            return 157;
                    else
                                    if (linear <= 1785435)
                        return 158;
                    else
                        return 159;
                else
                    if (linear <= 1970503)
                    if (linear <= 1880596)
                        if (linear <= 1837524)
                            if (linear <= 1816456)
                                if (linear <= 1806038)
                                    return 160;
                                else
                                    return 161;
                            else
                                if (linear <= 1826951)
                                return 162;
                            else
                                return 163;
                        else
                            if (linear <= 1858904)
                            if (linear <= 1848175)
                                return 164;
                            else
                                return 165;
                        else
                                if (linear <= 1869711)
                            return 166;
                        else
                            return 167;
                    else
                        if (linear <= 1924921)
                        if (linear <= 1902602)
                            if (linear <= 1891560)
                                return 168;
                            else
                                return 169;
                        else
                            if (linear <= 1913722)
                            return 170;
                        else
                            return 171;
                    else
                            if (linear <= 1947554)
                        if (linear <= 1936198)
                            return 172;
                        else
                            return 173;
                    else
                                if (linear <= 1958989)
                        return 174;
                    else
                        return 175;
                else
                        if (linear <= 2065466)
                    if (linear <= 2017350)
                        if (linear <= 1993768)
                            if (linear <= 1982096)
                                return 176;
                            else
                                return 177;
                        else
                            if (linear <= 2005519)
                            return 178;
                        else
                            return 179;
                    else
                        if (linear <= 2041249)
                        if (linear <= 2029260)
                            return 180;
                        else
                            return 181;
                    else
                            if (linear <= 2053318)
                        return 182;
                    else
                        return 183;
                else
                            if (linear <= 2114858)
                    if (linear <= 2090002)
                        if (linear <= 2077694)
                            return 184;
                        else
                            return 185;
                    else
                        if (linear <= 2102390)
                        return 186;
                    else
                        return 187;
                else
                                if (linear <= 2140033)
                    if (linear <= 2127406)
                        return 188;
                    else
                        return 189;
                else
                                    if (linear <= 2152742)
                    return 190;
                else
                    return 191;
            else
                    if (linear <= 2617649)
                if (linear <= 2381134)
                    if (linear <= 2270739)
                        if (linear <= 2217489)
                            if (linear <= 2191348)
                                if (linear <= 2178399)
                                    return 192;
                                else
                                    return 193;
                            else
                                if (linear <= 2204378)
                                return 194;
                            else
                                return 195;
                        else
                            if (linear <= 2243952)
                            if (linear <= 2230680)
                                return 196;
                            else
                                return 197;
                        else
                                if (linear <= 2257305)
                            return 198;
                        else
                            return 199;
                    else
                        if (linear <= 2325286)
                        if (linear <= 2297850)
                            if (linear <= 2284254)
                                return 200;
                            else
                                return 201;
                        else
                            if (linear <= 2311527)
                            return 202;
                        else
                            return 203;
                    else
                            if (linear <= 2353047)
                        if (linear <= 2339125)
                            return 204;
                        else
                            return 205;
                    else
                                if (linear <= 2367050)
                        return 206;
                    else
                        return 207;
                else
                    if (linear <= 2496758)
                    if (linear <= 2438290)
                        if (linear <= 2409549)
                            if (linear <= 2395301)
                                return 208;
                            else
                                return 209;
                        else
                            if (linear <= 2423878)
                            return 210;
                        else
                            return 211;
                    else
                        if (linear <= 2467360)
                        if (linear <= 2452784)
                            return 212;
                        else
                            return 213;
                    else
                            if (linear <= 2482018)
                        return 214;
                    else
                        return 215;
                else
                        if (linear <= 2556543)
                    if (linear <= 2526485)
                        if (linear <= 2511580)
                            return 216;
                        else
                            return 217;
                    else
                        if (linear <= 2541473)
                        return 218;
                    else
                        return 219;
                else
                            if (linear <= 2586930)
                    if (linear <= 2571695)
                        return 220;
                    else
                        return 221;
                else
                                if (linear <= 2602248)
                    return 222;
                else
                    return 223;
            else
                        if (linear <= 2875388)
                if (linear <= 2743847)
                    if (linear <= 2680082)
                        if (linear <= 2648700)
                            if (linear <= 2633133)
                                return 224;
                            else
                                return 225;
                        else
                            if (linear <= 2664349)
                            return 226;
                        else
                            return 227;
                    else
                        if (linear <= 2711798)
                        if (linear <= 2695898)
                            return 228;
                        else
                            return 229;
                    else
                            if (linear <= 2727781)
                        return 230;
                    else
                        return 231;
                else
                    if (linear <= 2808947)
                    if (linear <= 2776230)
                        if (linear <= 2759996)
                            return 232;
                        else
                            return 233;
                    else
                        if (linear <= 2792547)
                        return 234;
                    else
                        return 235;
                else
                        if (linear <= 2842000)
                    if (linear <= 2825432)
                        return 236;
                    else
                        return 237;
                else
                            if (linear <= 2858652)
                    return 238;
                else
                    return 239;
            else
                            if (linear <= 3012309)
                if (linear <= 2943174)
                    if (linear <= 2909113)
                        if (linear <= 2892208)
                            return 240;
                        else
                            return 241;
                    else
                        if (linear <= 2926101)
                        return 242;
                    else
                        return 243;
                else
                    if (linear <= 2977573)
                    if (linear <= 2960331)
                        return 244;
                    else
                        return 245;
                else
                        if (linear <= 2994899)
                    return 246;
                else
                    return 247;
            else
                                if (linear <= 3082799)
                if (linear <= 3047385)
                    if (linear <= 3029805)
                        return 248;
                    else
                        return 249;
                else
                    if (linear <= 3065049)
                    return 250;
                else
                    return 251;
            else
                                    if (linear <= 3118553)
                if (linear <= 3100633)
                    return 252;
                else
                    return 253;
            else
                                        if (linear <= 3136557)
                return 254;
            else
                return 255;
        }
    }
}
