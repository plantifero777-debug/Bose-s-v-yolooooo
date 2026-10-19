// BossZeepvolution - "Evolución de Boss" (reconstruido a partir del .dll original)
// Cambios respecto al original:
//   * Daño de cada habilidad configurable en JSON (Dano, Cantidad, Velocidad, ...)
//   * Vida de cada boss configurable en JSON (multiplicador o vida fija)
//   * Se quitó Thread.Sleep en las embestidas (bloqueaba el hilo del servidor)
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace BossZeepvolution
{
    // ───────────────────────── CONFIG ─────────────────────────
    public class SkillCfg
    {
        public bool Activa = true;
        public int Dano = 0;                       // daño base del proyectil (0 = no aplica)
        public int Cantidad = 1;                   // nº de proyectiles / NPCs / repeticiones
        public float Velocidad = 0f;               // velocidad del proyectil o de la embestida
        public float MultiplicadorDanoContacto = 1f; // solo embestidas: multiplica el daño por contacto del boss
    }

    public class BossCfg
    {
        public string Nombre = "";
        public float VidaMultiplicador = 5f;       // x veces la vida original
        public int VidaFija = 0;                   // si es > 0 ignora el multiplicador y usa este valor
    }

    public class Config
    {
        public float MultiplicadorDanoGlobalHabilidades = 1f;
        public int ProbabilidadBase = 180;         // 1 en N por tick por habilidad (menor = más frecuente)
        public float VidaMultiplicadorPorDefecto = 5f; // bosses que no aparezcan en "Bosses"
        public Dictionary<string, BossCfg> Bosses = new();
        public Dictionary<string, SkillCfg> Habilidades = new();

        public static string Ruta => Path.Combine(TShock.SavePath, "BossZeepvolution.json");

        public static Config Cargar()
        {
            Config c = null;
            try { if (File.Exists(Ruta)) c = JsonConvert.DeserializeObject<Config>(File.ReadAllText(Ruta)); }
            catch (Exception e) { TShock.Log.ConsoleError($"[BossZeepvolution] JSON inválido, uso valores por defecto: {e.Message}"); }
            c ??= new Config();
            // completar lo que falte con los valores por defecto (sin pisar lo que ya editaste)
            foreach (var (id, nombre) in Defaults.Bosses)
                if (!c.Bosses.ContainsKey(id.ToString())) c.Bosses[id.ToString()] = new BossCfg { Nombre = nombre, VidaMultiplicador = 5f };
            foreach (var (nombre, cfg) in Defaults.Habilidades)
                if (!c.Habilidades.ContainsKey(nombre)) c.Habilidades[nombre] = cfg;
            File.WriteAllText(Ruta, JsonConvert.SerializeObject(c, Formatting.Indented));
            return c;
        }
    }

    static class Defaults
    {
        public static readonly (int, string)[] Bosses =
        {
            (50,"King Slime"),(4,"Eye of Cthulhu"),(14,"Eater of Worlds"),(266,"Brain of Cthulhu"),(222,"Queen Bee"),
            (35,"Skeletron"),(113,"Wall of Flesh"),(668,"Deerclops"),(657,"Queen Slime"),(134,"The Destroyer"),
            (126,"Spazmatism"),(125,"Retinazer"),(127,"Skeletron Prime"),(262,"Plantera"),(245,"Golem"),
            (370,"Duke Fishron"),(636,"Empress of Light"),(439,"Lunatic Cultist"),(396,"Moon Lord (cabeza)"),
            (564,"Dark Mage"),(576,"Ogre"),(551,"Betsy"),(491,"Flying Dutchman"),(325,"Mourning Wood"),
            (327,"Pumpking"),(345,"Ice Queen"),(346,"Santa-NK1"),(344,"Everscream"),(392,"Martian Saucer")
        };

        static SkillCfg S(int dano, int cant, float vel, float contacto = 1f) =>
            new SkillCfg { Dano = dano, Cantidad = cant, Velocidad = vel, MultiplicadorDanoContacto = contacto };

        // Valores pensados para Modo Maestro en skyblock: en Maestro el daño enemigo se multiplica (~x3),
        // así que el daño base es bajo y las ráfagas son más cortas.
        public static readonly Dictionary<string, SkillCfg> Habilidades = new()
        {
            ["Teletransporte Real"]   = S(0, 1, 0),
            ["Embestida Real"]        = S(0, 1, 18, 1.2f),
            ["Lluvia de Cristales"]   = S(10, 6, 14),
            ["Ataque Veloz"]          = S(0, 1, 20, 1.2f),
            ["Ejército de Clones"]    = S(0, 3, 0),
            ["Tormenta de Aguijones"] = S(8, 6, 12),
            ["Lanzamiento de Huesos"] = S(12, 4, 15),
            ["Barrera de Láseres"]    = S(14, 3, 12),
            ["Invocar Probes"]        = S(0, 2, 0),
            ["Láser de Muerte"]       = S(16, 3, 15),
            ["Lluvia de Semillas"]    = S(10, 6, 14),
            ["Ataque de Burbujas"]    = S(14, 5, 16),
            ["Carga de Tiburón"]      = S(10, 6, 25, 1.2f),
            ["Esfera Fantasmal"]      = S(18, 3, 10),
            ["Embestida Cósmica"]     = S(0, 1, 22, 1.2f),
            ["Tormenta de Hielo"]     = S(14, 4, 12),
            ["Energía Oscura"]        = S(12, 1, 12),
            ["Aliento de Dragón"]     = S(14, 5, 14),
            ["Bombardeo de Cañones"]  = S(14, 4, 10),
            ["Guadaña Llameante"]     = S(16, 1, 16),
            ["Ola de Escarcha"]       = S(14, 5, 10),
            ["Rayo de la Muerte"]     = S(20, 1, 20),
            ["Rayos Prismáticos"]     = S(16, 6, 16),
        };
    }

    // ───────────────────────── HABILIDADES ─────────────────────────
    public class Habilidad
    {
        public int TipoBoss;
        public string Nombre;
        public int Dificultad;
        public Action<NPC, Player, SkillCfg> Ejecutar;
        public bool EsMovimiento => Nombre.Contains("Teleport") || Nombre.Contains("Dash") || Nombre.Contains("Jump") || Nombre.Contains("Charge");
    }

    public class Fase
    {
        public float PorcentajeVida;
        public List<Habilidad> HabilidadesFase = new();
    }

    [ApiVersion(2, 1)]
    public class PluginEvolucionBoss : TerrariaPlugin
    {
        public override string Name => "Evolución de Boss (arreglo Zeep)";
        public override string Author => "GILX_Dev, TERRARIAVUI + Zeep72";
        public override string Description => "Permite que los bosses evolucionen con habilidades aleatorias";

        static readonly Random aleatorio = new();
        Config cfg;
        readonly Dictionary<int, Habilidad[]> habilidadesActuales = new();
        readonly Dictionary<int, int[]> enfriamientos = new();
        readonly Dictionary<int, string> apodosBoss = new();
        readonly Dictionary<int, List<Habilidad>> patronesHabilidad = new(); // por TipoBoss
        readonly Dictionary<int, List<Fase>> fasesBoss = new();
        readonly Dictionary<int, float[]> IAPersonalizada = new(); // [0]=puede saltar, [1]=potencia esbirros
        readonly Dictionary<int, (int dano, int ticks)> contactoTemporal = new();

        readonly List<int> bossesVoladores = new() { 4, 222, 134, 126, 125, 370, 396, 636, 657, 551, 491, 345, 392 };
        readonly List<int> bossesInvocadores = new() { 266, 222, 134, 262, 245, 370, 564, 327, 346 };

        public PluginEvolucionBoss(Main game) : base(game) { }

        public override void Initialize()
        {
            cfg = Config.Cargar();
            InicializarHabilidadesBoss();
            ServerApi.Hooks.NpcSpawn.Register(this, AlAparecerNPC);
            ServerApi.Hooks.GameUpdate.Register(this, EnActualizacionNPC);
            ServerApi.Hooks.NpcKilled.Register(this, AlMorirNPC);
            Commands.ChatCommands.Add(new Command("bosszeep.admin", (a) =>
            {
                cfg = Config.Cargar();
                a.Player.SendSuccessMessage("[Evolución Boss] Configuración recargada.");
            }, "bossreload"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NpcSpawn.Deregister(this, AlAparecerNPC);
                ServerApi.Hooks.GameUpdate.Deregister(this, EnActualizacionNPC);
                ServerApi.Hooks.NpcKilled.Deregister(this, AlMorirNPC);
            }
            base.Dispose(disposing);
        }

        // ── hooks ──
        void AlAparecerNPC(NpcSpawnEventArgs args)
        {
            NPC npc = Main.npc[args.NpcId];
            if (!npc.boss) return;

            AplicarVida(npc);
            AleatorioHabilidades(npc);
            if (!habilidadesActuales.ContainsKey(npc.whoAmI)) habilidadesActuales.Add(npc.whoAmI, new Habilidad[8]);
            GenerarApodoBoss(npc, habilidadesActuales[npc.whoAmI]);
        }

        void EnActualizacionNPC(EventArgs args)
        {
            try
            {
                RestaurarContacto();
                foreach (NPC npc in Main.npc)
                {
                    if (npc.boss && npc.active)
                    {
                        Player objetivo = Main.player[npc.target];
                        ActualizarEnfriamientos(npc);
                        AplicarHabilidades(npc, objetivo);
                    }
                }
            }
            catch { }
        }

        void AlMorirNPC(NpcKilledEventArgs args)
        {
            NPC npc = Main.npc[args.npc.whoAmI];
            if (!npc.boss) return;
            if (fasesBoss.ContainsKey(npc.whoAmI))
            {
                float suma = 0f; int n = 0;
                foreach (var fase in fasesBoss[npc.whoAmI])
                    foreach (var h in fase.HabilidadesFase) { suma += h.Dificultad; n++; }
                float media = suma / n;
                npc.value *= 1f + media * 0.5f;
                habilidadesActuales.Remove(npc.whoAmI);
                enfriamientos.Remove(npc.whoAmI);
                IAPersonalizada.Remove(npc.whoAmI);
                fasesBoss.Remove(npc.whoAmI);
                apodosBoss.Remove(npc.whoAmI);
            }
        }

        // ── vida configurable ──
        void AplicarVida(NPC npc)
        {
            BossCfg b = cfg.Bosses.TryGetValue(npc.type.ToString(), out var bc) ? bc : null;
            long nueva;
            if (b != null && b.VidaFija > 0) nueva = b.VidaFija;
            else nueva = (long)(npc.lifeMax * (b != null ? b.VidaMultiplicador : cfg.VidaMultiplicadorPorDefecto));
            nueva = Math.Max(1, Math.Min(int.MaxValue, nueva));
            npc.lifeMax = (int)nueva;
            npc.life = npc.lifeMax;
            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npc.whoAmI);
        }

        // ── selección de habilidades (cada boss toma habilidades de OTROS bosses) ──
        List<int> ObtenerListaBosses() => new()
        {
            50,4,14,266,222,35,113,668,657,134,126,125,127,262,245,370,636,439,396,564,576,551,491,325,327,345,346,344,392
        };

        void AleatorioHabilidades(NPC boss)
        {
            if (habilidadesActuales.ContainsKey(boss.whoAmI))
            {
                habilidadesActuales.Remove(boss.whoAmI);
                enfriamientos.Remove(boss.whoAmI);
                IAPersonalizada.Remove(boss.whoAmI);
                fasesBoss.Remove(boss.whoAmI);
            }
            var otros = ObtenerListaBosses().Where(t => t != boss.type).ToList();
            bool hardmode = EsBossHardmode(boss.type);
            int n1 = 5, n2 = 6, n3 = hardmode ? 8 : 7;
            var f1 = AleatorioHabilidadesFase(otros, n1);
            var f2 = AleatorioHabilidadesFase(otros, n2);
            var f3 = AleatorioHabilidadesFase(otros, n3);
            var todas = new List<Habilidad>(); todas.AddRange(f1); todas.AddRange(f2); todas.AddRange(f3);
            habilidadesActuales[boss.whoAmI] = todas.GroupBy(h => h.TipoBoss).Select(g => g.First()).ToArray();
            fasesBoss[boss.whoAmI] = new List<Fase>
            {
                new Fase { PorcentajeVida = 1f,   HabilidadesFase = f1 },
                new Fase { PorcentajeVida = 0.5f, HabilidadesFase = f2 },
                new Fase { PorcentajeVida = 0.3f, HabilidadesFase = f3 },
            };
            enfriamientos[boss.whoAmI] = new int[hardmode ? 8 : 7];
            bool tieneVolador = todas.Any(h => bossesVoladores.Contains(h.TipoBoss));
            bool tieneInvocador = todas.Any(h => bossesInvocadores.Contains(h.TipoBoss));
            IAPersonalizada[boss.whoAmI] = new float[] { tieneVolador ? 0f : 1f, tieneInvocador ? 0f : 1f };
        }

        List<Habilidad> AleatorioHabilidadesFase(List<int> tipos, int cantidad)
        {
            var res = new List<Habilidad>();
            for (int i = 0; i < cantidad; i++)
            {
                int tipo = tipos[aleatorio.Next(tipos.Count)];
                if (!patronesHabilidad.TryGetValue(tipo, out var lista) || lista.Count == 0) continue;
                var cand = lista;
                if (tipo == 113) cand = lista.Where(h => !h.EsMovimiento).ToList();
                if (cand.Count > 0) res.Add(cand[aleatorio.Next(cand.Count)]);
            }
            return res;
        }

        void ActualizarEnfriamientos(NPC boss)
        {
            if (!enfriamientos.TryGetValue(boss.whoAmI, out var cd)) return;
            for (int i = 0; i < cd.Length; i++) if (cd[i] > 0) cd[i]--;
        }

        void AplicarHabilidades(NPC boss, Player objetivo)
        {
            if (!fasesBoss.ContainsKey(boss.whoAmI)) return;
            if (objetivo == null || !objetivo.active || objetivo.dead) return;

            float porcentajeVida = (float)boss.life / boss.lifeMax;
            float distancia = Vector2.Distance(boss.Center, objetivo.Center);
            var fase = fasesBoss[boss.whoAmI].OrderBy(f => f.PorcentajeVida).FirstOrDefault(f => porcentajeVida <= f.PorcentajeVida);
            if (fase == null) return;

            for (int i = 0; i < fase.HabilidadesFase.Count; i++)
            {
                if (!enfriamientos.ContainsKey(boss.whoAmI)) continue;
                var cd = enfriamientos[boss.whoAmI];
                if (i < cd.Length && cd[i] > 0) continue;
                if (i >= cd.Length) continue;

                var hab = fase.HabilidadesFase[i];
                if (!cfg.Habilidades.TryGetValue(hab.Nombre, out var sc) || !sc.Activa) continue;

                int prob = cfg.ProbabilidadBase;
                if (porcentajeVida < 0.5f) prob = (int)(prob * 0.8f);
                if (porcentajeVida < 0.3f) prob = (int)(prob * 0.7f);
                if (distancia > 300f && distancia < 600f) prob = (int)(prob * 0.9f);
                if (porcentajeVida <= 0.5f) prob = (int)(prob * 0.85f);
                if (porcentajeVida <= 0.3f) prob = (int)(prob * 0.7f);
                prob = Math.Max(1, (int)(prob * (1f - hab.Dificultad * 0.1f)));

                if (Main.rand.Next(prob) == 0)
                {
                    Console.WriteLine($"[Evolución Boss] {apodosBoss.GetValueOrDefault(boss.whoAmI, boss.FullName)} usa {hab.Nombre}!");
                    Console.WriteLine($"[Evolución Boss] Probabilidad: 1/{prob} (Vida: {porcentajeVida * 100f:F0}%, Distancia: {distancia:F0})");
                    hab.Ejecutar(boss, objetivo, sc);
                    cd[i] = 30;
                    if (porcentajeVida < 0.3f) cd[i] = 21;
                }
            }

            // salto alto para bosses terrestres sin habilidades voladoras
            if (!bossesVoladores.Contains(boss.type) && IAPersonalizada.TryGetValue(boss.whoAmI, out var ia) && ia[0] == 1f)
            {
                if (boss.velocity.Y == 0f && Main.rand.Next(120) == 0)
                {
                    boss.velocity.Y = -12f;
                    Console.WriteLine($"[Evolución Boss] {apodosBoss.GetValueOrDefault(boss.whoAmI, boss.FullName)} realiza un salto alto!");
                }
            }

            // esbirros más agresivos si el boss no tiene habilidades de invocación
            if (IAPersonalizada.TryGetValue(boss.whoAmI, out var ia2) && ia2[1] == 1f)
            {
                for (int n = 0; n < Main.maxNPCs; n++)
                {
                    NPC m = Main.npc[n];
                    if (!m.active) continue;
                    if (!(EsMinion(m.type) && Vector2.Distance(boss.Center, m.Center) < 800f)) continue;
                    m.ai[0] = 1f; m.ai[1] = boss.whoAmI;
                    Vector2 dir = (boss.Center - m.Center).SafeNormalize(Vector2.Zero);
                    float vel = 8f;
                    if (m.type == 139) vel = 10f;
                    if (m.type == 5) vel = 12f;
                    m.velocity = dir * vel;
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, n);
                    if (Main.rand.Next(30) == 0)
                    {
                        int polvo = 62;
                        if (m.type == 139) polvo = 226;
                        if (m.type == 5) polvo = 5;
                        Dust.NewDust(m.position, m.width, m.height, polvo, 0f, 0f, 0, default, 1f);
                    }
                }
            }
        }

        // ── apodo ──
        void GenerarApodoBoss(NPC boss, Habilidad[] habs)
        {
            try
            {
                string nombre = Lang.GetNPCNameValue(boss.type);
                var nombres = ObtenerListaBosses().Where(t => t != boss.type).OrderBy(_ => aleatorio.Next()).Take(4)
                    .Select(t => Lang.GetNPCNameValue(t)).ToList();
                string[] pre = { "Mutado","Evolucionado","Híbrido","Corrompido","Ascendido","Legendario","Divino","Inmortal","Supremo","Ancestral","Caótico","Místico","Cósmico","Primigenio","Retorcido" };
                string[] suf = {
                    "de " + nombres[0], "con el Poder de " + nombres[1], "Fusión con " + nombres[2], "Heredero de " + nombres[3],
                    "Combinado con " + nombres[0], "Empuñando el Poder de " + nombres[1], "Infundido con la Esencia de " + nombres[2], "Mejorado con el ADN de " + nombres[3] };
                apodosBoss[boss.whoAmI] = $"{pre[aleatorio.Next(pre.Length)]} {nombre} {suf[aleatorio.Next(suf.Length)]}";
            }
            catch { }
        }

        bool EsBossHardmode(int t) => t is 134 or 126 or 125 or 127 or 262 or 245 or 370 or 439 or 396;

        bool EsMinion(int t) => t is 267 or 139 or 210 or 247 or 248 or 372 or 373 or 5 or 6 or 666 or 246 or 454 or 211 or 400 or 396
            or 36 or 135 or 264 or 658 or 659 or 127 or 128 or 131 or 129 or 130;

        // ── helpers de daño ──
        int Dano(SkillCfg c) => (int)(c.Dano * cfg.MultiplicadorDanoGlobalHabilidades);

        void Proy(Vector2 pos, Vector2 vel, int tipo, SkillCfg c, bool hostil = true)
        {
            int idx = Projectile.NewProjectile(null, pos, vel, tipo, Dano(c), 2f, Main.myPlayer);
            if (idx >= 0 && idx < Main.maxProjectiles)
            {
                if (hostil) { Main.projectile[idx].hostile = true; Main.projectile[idx].friendly = false; }
                NetMessage.SendData((int)PacketTypes.ProjectileNew, -1, -1, null, idx);
            }
        }

        Vector2 Dir(NPC boss, Player t) => (t.Center - boss.Center).SafeNormalize(Vector2.Zero);
        Vector2 DirAleatoria() => new Vector2(aleatorio.Next(-10, 11), aleatorio.Next(-10, 11)).SafeNormalize(Vector2.Zero);
        void Polvo(NPC b, int tipo, int veces) { for (int i = 0; i < veces; i++) Dust.NewDust(b.position, b.width, b.height, tipo, 0f, 0f, 0, default, 1f); }
        void Sync(NPC b) => NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, b.whoAmI);

        void Embestida(NPC b, SkillCfg c, Vector2 dir, int polvo, int nPolvo)
        {
            b.velocity = dir * c.Velocidad;
            Polvo(b, polvo, nPolvo);
            if (c.MultiplicadorDanoContacto != 1f && !contactoTemporal.ContainsKey(b.whoAmI))
            {
                contactoTemporal[b.whoAmI] = (b.damage, 60);
                b.damage = (int)(b.damage * c.MultiplicadorDanoContacto);
            }
            Sync(b);
        }

        void RestaurarContacto()
        {
            foreach (var k in contactoTemporal.Keys.ToList())
            {
                var (dano, ticks) = contactoTemporal[k];
                if (ticks <= 1) { if (Main.npc[k].active) Main.npc[k].damage = dano; contactoTemporal.Remove(k); }
                else contactoTemporal[k] = (dano, ticks - 1);
            }
        }

        void Invocar(NPC boss, SkillCfg c, int tipo, bool disperso, bool vidaCompartida)
        {
            for (int i = 0; i < c.Cantidad; i++)
            {
                Vector2 p = boss.Center + (disperso ? new Vector2(aleatorio.Next(-200, 201), aleatorio.Next(-200, 201)) : Vector2.Zero);
                int idx = NPC.NewNPC(null, (int)p.X, (int)p.Y, tipo, 0, 0f, 0f, 0f, 0f, 255);
                if (idx < 0 || idx >= Main.maxNPCs) continue;
                if (vidaCompartida) { Main.npc[idx].realLife = boss.whoAmI; Main.npc[idx].defense /= 2; }
                NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, idx);
            }
        }

        // ── definición de las 23 habilidades ──
        void Add(int tipo, string nombre, int dif, Action<NPC, Player, SkillCfg> ejecutar)
        {
            if (!patronesHabilidad.ContainsKey(tipo)) patronesHabilidad[tipo] = new List<Habilidad>();
            patronesHabilidad[tipo].Add(new Habilidad { TipoBoss = tipo, Nombre = nombre, Dificultad = dif, Ejecutar = ejecutar });
        }

        void InicializarHabilidadesBoss()
        {
            Add(50, "Teletransporte Real", 2, (b, t, c) => { b.position = t.Center + new Vector2(0, -200); Polvo(b, 59, 50); Sync(b); });
            Add(50, "Embestida Real", 3, (b, t, c) => Embestida(b, c, Dir(b, t), 59, 15));
            Add(4, "Lluvia de Cristales", 4, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, DirAleatoria() * c.Velocidad, 90, c); });
            Add(4, "Ataque Veloz", 3, (b, t, c) => Embestida(b, c, Dir(b, t), 5, 20));
            Add(266, "Ejército de Clones", 4, (b, t, c) => Invocar(b, c, 267, true, true));
            Add(222, "Tormenta de Aguijones", 3, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, Dir(b, t).RotatedBy(aleatorio.NextDouble() * 0.6 - 0.3) * c.Velocidad, 55, c); });
            Add(35, "Lanzamiento de Huesos", 3, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, DirAleatoria() * c.Velocidad, 532, c); });
            Add(113, "Barrera de Láseres", 4, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, ((t.Center + new Vector2(0, aleatorio.Next(-200, 201))) - b.Center).SafeNormalize(Vector2.Zero) * c.Velocidad, 83, c); });
            Add(134, "Invocar Probes", 4, (b, t, c) => Invocar(b, c, 139, false, false));
            Add(125, "Láser de Muerte", 5, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, Dir(b, t).RotatedBy(aleatorio.NextDouble() * 0.4 - 0.2) * c.Velocidad, 100, c); });
            Add(262, "Lluvia de Semillas", 4, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, DirAleatoria() * c.Velocidad, 275, c, false); });
            Add(370, "Ataque de Burbujas", 5, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, DirAleatoria() * c.Velocidad, 410, c, false); });
            Add(370, "Carga de Tiburón", 4, (b, t, c) =>
            {
                Embestida(b, c, ((t.Center + b.velocity * 20f) - b.Center).SafeNormalize(Vector2.Zero), 5, 0);
                for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, Vector2.Zero, 410, c, false);
            });
            Add(396, "Esfera Fantasmal", 5, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center, Dir(b, t).RotatedBy(aleatorio.NextDouble() * 0.3 - 0.15) * c.Velocidad, 454, c); });
            Add(396, "Embestida Cósmica", 5, (b, t, c) =>
            {
                if (b.velocity != Vector2.Zero) b.Center = t.Center - Vector2.Normalize(b.velocity) * 200f;
                Embestida(b, c, Dir(b, t), 62, 30);
            });
            Add(668, "Tormenta de Hielo", 4, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(t.Center + new Vector2(aleatorio.Next(-200, 201), -600), new Vector2(aleatorio.Next(-3, 4), c.Velocidad), 263, c); });
            Add(564, "Energía Oscura", 3, (b, t, c) => Proy(b.Center, Dir(b, t) * c.Velocidad, 674, c));
            Add(551, "Aliento de Dragón", 5, (b, t, c) => { int mitad = c.Cantidad / 2; for (int i = -mitad; i <= mitad; i++) Proy(b.Center, Dir(b, t).RotatedBy(MathHelper.ToRadians(i * 15f)) * c.Velocidad, 686, c); });
            Add(491, "Bombardeo de Cañones", 4, (b, t, c) => { for (int i = 0; i < c.Cantidad; i++) Proy(b.Center + new Vector2(aleatorio.Next(-100, 101), 0), new Vector2(0, c.Velocidad), 240, c); });
            Add(327, "Guadaña Llameante", 4, (b, t, c) => Proy(b.Center, Dir(b, t) * c.Velocidad, 329, c));
            Add(345, "Ola de Escarcha", 5, (b, t, c) => { int mitad = c.Cantidad / 2; for (int i = -mitad; i <= mitad; i++) Proy(b.Center, new Vector2(i * 8f, -c.Velocidad), 174, c); });
            Add(392, "Rayo de la Muerte", 5, (b, t, c) => Proy(b.Center, Dir(b, t) * c.Velocidad, 435, c));
            Add(636, "Rayos Prismáticos", 5, (b, t, c) =>
            {
                for (int i = 0; i < c.Cantidad; i++)
                {
                    float a = i * MathHelper.TwoPi / c.Cantidad;
                    Proy(b.Center, new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * c.Velocidad, 873, c);
                }
            });
        }
    }
}
