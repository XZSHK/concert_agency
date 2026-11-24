-- Дополнительные концерты (афиши лежат в img/ рядом с проектом).
-- Выполните один раз в phpMyAdmin (вкладка SQL), база Concert_Agency.
-- Скрипт идемпотентен: повторный запуск не создаст дубликаты
-- (проверка по паре artist_group_name + event_date).
--
-- Залы назначаются автоматически по кругу из ЗАЛА с минимальными id:
-- сначала выполните SELECT COUNT(*) FROM concert_halls; (если 0 — сначала заведите залы).
-- При необходимости замените выражения вида
--   (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET N)
-- на конкретные id нужных вам залов из SELECT id, hall_name, city FROM concert_halls;

INSERT INTO concert_events
  (hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price, image_path)
SELECT src.hall_id, src.artist, src.event_date, src.age, src.parterre_price, src.vip_price, src.image_path
FROM (
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 0) AS hall_id,
         'Северное сияние' AS artist, '2026-12-05 20:00:00' AS event_date,
         '16+' AS age, 1800.00 AS parterre_price, 4800.00 AS vip_price,
         'afisha_severnoe_siyanie.jpg' AS image_path
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 1),
         'Мята Лемон', '2026-12-12 19:00:00', '12+', 1200.00, 3600.00,
         'afisha_myata_lemon.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 2),
         'Анна Волкова', '2026-12-19 19:30:00', '12+', 2500.00, 6500.00,
         'afisha_anna_volkova.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 0),
         'Монтевидео (джаз-квинтет)', '2027-01-09 19:00:00', '6+', 1500.00, 4000.00,
         'afisha_montevideo.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 1),
         'TIDAL ECHO', '2027-01-16 22:00:00', '18+', 2200.00, 7500.00,
         'afisha_tidal_echo.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 2),
         'Зимняя сказка (симфонический оркестр)', '2027-01-30 18:00:00', '6+', 2000.00, 5500.00,
         'afisha_zimnyaya_skazka.jpg'
) src
WHERE NOT EXISTS (
  SELECT 1 FROM concert_events e
  WHERE e.artist_group_name = src.artist AND e.event_date = src.event_date
);

-- Проверка: SELECT artist_group_name, event_date, image_path FROM concert_events ORDER BY event_date;
