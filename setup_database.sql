-- ═══════════════════════════════════════════════════════════════════
--  «Арт-Престиж» — ПОЛНАЯ установка базы данных Concert_Agency
--  (база + все 7 таблиц + демонстрационные записи)
--
--  КАК ЗАПУСТИТЬ: phpMyAdmin → вкладка SQL → вставить весь текст → «Вперёд».
--  Скрипт ИДЕМПОТЕНТНЫЙ: повторный запуск ничего не дублирует
--  (CREATE ... IF NOT EXISTS, вставки под WHERE NOT EXISTS).
--
--  ДАННЫЙ СКРИПТ НЕ УДАЛЯЕТ И НЕ ПЕРЕСОЗДАЁТ СУЩЕСТВУЮЩУЮ БАЗУ.
--  В существующей БД он: добавит недостающие таблицы/столбцы,
--  добавит недостающие записи, ничего не удалит.
--
--  Пароли ДЕМО (SHA2(...,256)):
--    manager / manager123 · producer / producer123
--    покупатели: ivan.petrov@example.com / buyer123 (и остальные @example.com)
--  Перед защитой/эксплуатацией смените пароли!
-- ═══════════════════════════════════════════════════════════════════

CREATE DATABASE IF NOT EXISTS Concert_Agency
  DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE Concert_Agency;

-- ───────────────────────── 1. Таблицы ─────────────────────────

-- Роли (по ТЗ: Менеджер билетной программы / Продюсер гастрольных туров / Клиент)
CREATE TABLE IF NOT EXISTS roles (
  id    INT AUTO_INCREMENT PRIMARY KEY,
  name  VARCHAR(100) NOT NULL UNIQUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Учётные записи сотрудников (SHA256 в password_hash)
CREATE TABLE IF NOT EXISTS users (
  id             INT AUTO_INCREMENT PRIMARY KEY,
  role_id        INT          NOT NULL,
  login          VARCHAR(100) NOT NULL UNIQUE,
  password_hash  VARCHAR(255) NOT NULL,
  full_name      VARCHAR(255) NOT NULL,
  CONSTRAINT fk_users_role  FOREIGN KEY (role_id) REFERENCES roles(id)
    ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Залы (справочник площадок)
CREATE TABLE IF NOT EXISTS concert_halls (
  id                 INT AUTO_INCREMENT PRIMARY KEY,
  hall_name          VARCHAR(255)    NOT NULL UNIQUE,
  city               VARCHAR(100)    NOT NULL,
  parterre_capacity  INT             NOT NULL,
  vip_capacity       INT             NOT NULL,
  base_rent_per_day  DECIMAL(12,2)   NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Концерты
CREATE TABLE IF NOT EXISTS concert_events (
  id                 INT AUTO_INCREMENT PRIMARY KEY,
  hall_id            INT             NOT NULL,
  artist_group_name  VARCHAR(255)    NOT NULL,
  event_date         DATETIME        NOT NULL,
  age_restriction    VARCHAR(10)     NOT NULL,
  parterre_price     DECIMAL(12,2)   NOT NULL,
  vip_price          DECIMAL(12,2)   NOT NULL,
  image_path         VARCHAR(512)    NULL,
  INDEX idx_event_date (event_date),
  CONSTRAINT fk_events_hall FOREIGN KEY (hall_id) REFERENCES concert_halls(id)
    ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Покупатели (контрагенты со стороны клиента)
CREATE TABLE IF NOT EXISTS ticket_buyers (
  id                     INT AUTO_INCREMENT PRIMARY KEY,
  full_name              VARCHAR(255) NOT NULL,
  email                  VARCHAR(255) NOT NULL UNIQUE,
  phone                  VARCHAR(32)          NULL,
  has_promo_subscription BOOLEAN      NOT NULL DEFAULT FALSE,
  password               VARCHAR(255) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Заказы (документ: заголовок). user_id допускает NULL
-- (заказ может быть оформлен онлайн, без сотрудника).
CREATE TABLE IF NOT EXISTS ticket_orders (
  id              BIGINT AUTO_INCREMENT PRIMARY KEY,
  order_reference VARCHAR(32)   NOT NULL UNIQUE,
  user_id         INT           NULL,
  buyer_id        INT           NOT NULL,
  event_id        INT           NOT NULL,
  order_datetime  DATETIME      NOT NULL,
  service_fee     DECIMAL(12,2) NOT NULL DEFAULT 0.00,
  total_amount    DECIMAL(12,2) NOT NULL,
  status          VARCHAR(64)   NOT NULL,
  cancel_reason   VARCHAR(500)  NULL,
  INDEX idx_orders_event  (event_id),
  INDEX idx_orders_buyer  (buyer_id),
  INDEX idx_orders_status (status),
  CONSTRAINT fk_orders_user  FOREIGN KEY (user_id)  REFERENCES users(id)
    ON UPDATE CASCADE ON DELETE SET NULL,
  CONSTRAINT fk_orders_buyer FOREIGN KEY (buyer_id) REFERENCES ticket_buyers(id)
    ON UPDATE CASCADE ON DELETE RESTRICT,
  CONSTRAINT fk_orders_event FOREIGN KEY (event_id) REFERENCES concert_events(id)
    ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Позиции заказа (спецификация документа)
CREATE TABLE IF NOT EXISTS ticket_order_seats (
  id               INT AUTO_INCREMENT PRIMARY KEY,
  order_id         BIGINT        NOT NULL,
  sector_type      VARCHAR(50)   NOT NULL,
  row_num          INT           NOT NULL,
  seat_num         INT           NOT NULL,
  base_seat_price  DECIMAL(12,2) NOT NULL,
  final_seat_cost  DECIMAL(12,2) NOT NULL,
  INDEX idx_seats_order (order_id),
  CONSTRAINT fk_seats_order FOREIGN KEY (order_id) REFERENCES ticket_orders(id)
    ON UPDATE CASCADE ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Для старых БД без image_path: добавляем столбец, если его нет
SET @has_image_path := (
  SELECT COUNT(*) FROM information_schema.columns
  WHERE table_schema = 'Concert_Agency' AND table_name = 'concert_events' AND column_name = 'image_path'
);
SET @ddl := IF(@has_image_path = 0,
  'ALTER TABLE concert_events ADD COLUMN image_path VARCHAR(512) NULL AFTER vip_price',
  'SELECT "image_path: столбец уже есть" AS ok');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ──────────────────── 2. Роли и пользователи ────────────────────

INSERT INTO roles (name)
SELECT 'Менеджер билетной программы' WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Менеджер билетной программы');
INSERT INTO roles (name)
SELECT 'Продюсер гастрольных туров' WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Продюсер гастрольных туров');
INSERT INTO roles (name)
SELECT 'Клиент' WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Клиент');

-- Сотрудники (пароли SHA2; логины для входа)
INSERT INTO users (role_id, login, password_hash, full_name)
SELECT r.id, 'manager', SHA2('manager123', 256), 'Менеджер билетной программы'
FROM roles r WHERE r.name = 'Менеджер билетной программы'
  AND NOT EXISTS (SELECT 1 FROM users WHERE login = 'manager');

INSERT INTO users (role_id, login, password_hash, full_name)
SELECT r.id, 'producer', SHA2('producer123', 256), 'Продюсер гастрольных туров'
FROM roles r WHERE r.name = 'Продюсер гастрольных туров'
  AND NOT EXISTS (SELECT 1 FROM users WHERE login = 'producer');

-- Технический аккаунт онлайн-канала (заказы покупателей через сайт/приложение)
INSERT INTO users (role_id, login, password_hash, full_name)
SELECT r.id, 'client', SHA2('online-channel-internal', 256), 'Клиент (онлайн-заказ)'
FROM roles r WHERE r.name = 'Клиент'
  AND NOT EXISTS (SELECT 1 FROM users WHERE login = 'client');

-- ──────────────────────── 3. Залы ────────────────────────

INSERT INTO concert_halls (hall_name, city, parterre_capacity, vip_capacity, base_rent_per_day)
SELECT t.hall_name, t.city, t.p, t.v, t.rent
FROM (
  SELECT 'Кремлёвский зал Кемерово' AS hall_name, 'Кемерово' AS city, 1200 AS p, 250 AS v, 250000.00 AS rent
  UNION ALL SELECT 'Дворец культуры «Горизонт»', 'Новокузнецк', 900, 180, 150000.00
  UNION ALL SELECT 'Арена Северная', 'Кемерово', 5000, 800, 600000.00
  UNION ALL SELECT 'Кузбасская филармония', 'Кемерово', 800, 300, 320000.00
  UNION ALL SELECT 'Дворец спорта «Прометей»', 'Кемерово', 3500, 500, 450000.00
  UNION ALL SELECT 'Дом актёрского мастерства', 'Ленинск-Кузнецкий', 400, 80, 90000.00
) t
WHERE NOT EXISTS (SELECT 1 FROM concert_halls h WHERE h.hall_name = t.hall_name);

-- ─────────────────────── 4. Концерты ───────────────────────
-- Афиши — из img/ репозитория (image_path = имя файла); там, где файла нет, NULL (показан placeholder).

INSERT INTO concert_events (hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price, image_path)
SELECT src.hall_id, src.artist, src.event_date, src.age, src.p, src.v, src.img
FROM (
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 0) AS hall_id,
         'Северное сияние' AS artist, '2026-12-05 20:00:00' AS event_date, '16+' AS age,
         1800.00 AS p, 4800.00 AS v, 'afisha_severnoe_siyanie.jpg' AS img
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 1),
         'Мята Лемон', '2026-12-12 19:00:00', '12+', 1200.00, 3600.00, 'afisha_myata_lemon.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 2),
         'Анна Волкова', '2026-12-19 19:30:00', '12+', 2500.00, 6500.00, 'afisha_anna_volkova.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 3),
         'Оркестр русских народных инструментов', '2026-12-26 19:00:00', '6+', 900.00, 2800.00, 'afisha_russkie_narodnye.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 0),
         'Монтевидео (джаз-квинтет)', '2027-01-09 19:00:00', '6+', 1500.00, 4000.00, 'afisha_montevideo.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 1),
         'TIDAL ECHO', '2027-01-16 22:00:00', '18+', 2200.00, 7500.00, 'afisha_tidal_echo.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 2),
         'Зимняя сказка (симфонический оркестр)', '2027-01-30 18:00:00', '6+', 2000.00, 5500.00, 'afisha_zimnyaya_skazka.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 3),
         'Классическая симфония', '2027-02-13 19:00:00', '6+', 1600.00, 4200.00, 'afisha_klassicheskaya_simfoniya.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 4),
         'Этно Кузбасс — фольклорный ансамбль', '2027-02-27 18:30:00', '6+', 700.00, 2200.00, 'afisha_etno_kuzbass.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 5),
         'Таисия Полякова — камерный вечер', '2027-03-14 19:00:00', '12+', 1400.00, 3900.00, 'afisha_taisia_polyakova.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 0),
         'Космос: планетарий-шоу с оркестром', '2027-03-28 20:00:00', '6+', 1700.00, 4600.00, 'afisha_kosmos_shou.jpg'
  UNION ALL
  SELECT (SELECT id FROM concert_halls ORDER BY id LIMIT 1 OFFSET 4),
         'Весенний балет «Лебединое озеро»', '2027-04-11 19:00:00', '12+', 2800.00, 7800.00, 'afisha_lebedinoe_ozero.jpg'
) src
WHERE NOT EXISTS (
  SELECT 1 FROM concert_events e WHERE e.artist_group_name = src.artist AND e.event_date = src.event_date
);

-- ────────────────────── 5. Покупатели ──────────────────────

INSERT INTO ticket_buyers (full_name, email, phone, has_promo_subscription, password)
SELECT t.full_name, t.email, t.phone, t.promo, SHA2('buyer123', 256)
FROM (
  SELECT 'Иван Петров' AS full_name, 'ivan.petrov@example.com' AS email, '+7 (900) 111-22-33' AS phone, TRUE  AS promo
  UNION ALL SELECT 'Мария Смирнова', 'maria.smirnova@example.com', '+7 (900) 222-33-44', FALSE
  UNION ALL SELECT 'Дмитрий Кузнецов', 'd.kuznetsov@example.com', '+7 (900) 333-44-55', TRUE
  UNION ALL SELECT 'Елена Васильева', 'elena.vas@example.com', '+7 (900) 444-55-66', FALSE
  UNION ALL SELECT 'Артём Морозов', 'a.morozov@example.com', '+7 (900) 555-66-77', FALSE
) t
WHERE NOT EXISTS (SELECT 1 FROM ticket_buyers b WHERE b.email = t.email);

-- ─────────── 6. Заказы и позиции (FSM-статусы разные) ───────────
-- user_id для онлайн-заказов = технический аккаунт 'Клиент', для кассовых — 'manager'.
-- Суммы посчитаны по формуле ТЗ:
--   позиция VIP = базовый VIP + 500 (бейдж);
--   сбор 10% (промокод подписчика обнуляет его).

INSERT INTO ticket_orders (order_reference, user_id, buyer_id, event_id, order_datetime, service_fee, total_amount, status, cancel_reason)
SELECT src.ref, src.uid, src.bid, src.eid, src.dt, src.fee, src.total, src.status, src.reason
FROM (
  SELECT 'PR23-KTZ-2026-00001' AS ref,
         (SELECT id FROM users WHERE login = 'client') AS uid,
         (SELECT id FROM ticket_buyers WHERE email = 'ivan.petrov@example.com') AS bid,
         (SELECT id FROM concert_events WHERE artist_group_name = 'Мята Лемон') AS eid,
         '2026-11-18 10:15:00' AS dt, 0.00 AS fee, 5300.00 AS total,
         'Бронь (15 мин)' AS status, NULL AS reason
  UNION ALL SELECT 'PR23-KTZ-2026-00002', (SELECT id FROM users WHERE login = 'manager'),
         (SELECT id FROM ticket_buyers WHERE email = 'maria.smirnova@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Анна Волкова'),
         '2026-11-18 12:40:00', 1400.00, 15400.00, 'Оплачен', NULL
  UNION ALL SELECT 'PR23-KTZ-2026-00003', (SELECT id FROM users WHERE login = 'client'),
         (SELECT id FROM ticket_buyers WHERE email = 'd.kuznetsov@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Анна Волкова'),
         '2026-11-19 09:05:00', 0.00, 2500.00, 'Выписан', NULL
  UNION ALL SELECT 'PR23-KTZ-2026-00004', (SELECT id FROM users WHERE login = 'client'),
         (SELECT id FROM ticket_buyers WHERE email = 'elena.vas@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Северное сияние'),
         '2026-11-19 15:30:00', 180.00, 1980.00, 'Возврат билета',
         'Покупатель отказался от поездки, возврат оформлен по заявлению'
  UNION ALL SELECT 'PR23-KTZ-2026-00005', (SELECT id FROM users WHERE login = 'manager'),
         (SELECT id FROM ticket_buyers WHERE email = 'a.morozov@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Монтевидео (джаз-квинтет)'),
         '2026-11-20 11:00:00', 750.00, 8250.00, 'Контроль на входе пройден', NULL
  UNION ALL SELECT 'PR23-KTZ-2026-00006', (SELECT id FROM users WHERE login = 'client'),
         (SELECT id FROM ticket_buyers WHERE email = 'maria.smirnova@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'TIDAL ECHO'),
         '2026-11-20 13:25:00', 800.00, 8800.00, 'Бронь (15 мин)', NULL
  UNION ALL SELECT 'PR23-KTZ-2026-00007', (SELECT id FROM users WHERE login = 'manager'),
         (SELECT id FROM ticket_buyers WHERE email = 'elena.vas@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Зимняя сказка (симфонический оркестр)'),
         '2026-11-21 16:45:00', 400.00, 4400.00, 'Оплачен', NULL
  UNION ALL SELECT 'PR23-KTZ-2026-00008', (SELECT id FROM users WHERE login = 'client'),
         (SELECT id FROM ticket_buyers WHERE email = 'ivan.petrov@example.com'),
         (SELECT id FROM concert_events WHERE artist_group_name = 'Зимняя сказка (симфонический оркестр)'),
         '2026-11-22 08:20:00', 0.00, 6000.00, 'Выписан', NULL
) src
WHERE NOT EXISTS (SELECT 1 FROM ticket_orders o WHERE o.order_reference = src.ref);

INSERT INTO ticket_order_seats (order_id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost)
SELECT o.id, s.sector, s.row_num, s.seat_num, s.base, s.final
FROM (
  SELECT 'PR23-KTZ-2026-00001' AS ref, 'Партер' AS sector, 1 AS row_num, 1 AS seat_num, 1200.00 AS base, 1200.00 AS final
  UNION ALL SELECT 'PR23-KTZ-2026-00001', 'VIP', 1, 1, 3600.00, 4100.00
  UNION ALL SELECT 'PR23-KTZ-2026-00002', 'VIP', 1, 1, 6500.00, 7000.00
  UNION ALL SELECT 'PR23-KTZ-2026-00002', 'VIP', 1, 2, 6500.00, 7000.00
  UNION ALL SELECT 'PR23-KTZ-2026-00003', 'Партер', 1, 1, 2500.00, 2500.00
  UNION ALL SELECT 'PR23-KTZ-2026-00004', 'Партер', 1, 1, 1800.00, 1800.00
  UNION ALL SELECT 'PR23-KTZ-2026-00005', 'Партер', 1, 1, 1500.00, 1500.00
  UNION ALL SELECT 'PR23-KTZ-2026-00005', 'Партер', 1, 2, 1500.00, 1500.00
  UNION ALL SELECT 'PR23-KTZ-2026-00005', 'VIP', 1, 1, 4000.00, 4500.00
  UNION ALL SELECT 'PR23-KTZ-2026-00006', 'VIP', 1, 1, 7500.00, 8000.00
  UNION ALL SELECT 'PR23-KTZ-2026-00007', 'Партер', 1, 1, 2000.00, 2000.00
  UNION ALL SELECT 'PR23-KTZ-2026-00007', 'Партер', 1, 2, 2000.00, 2000.00
  UNION ALL SELECT 'PR23-KTZ-2026-00008', 'VIP', 1, 1, 5500.00, 6000.00
) s
JOIN ticket_orders o ON o.order_reference = s.ref
WHERE NOT EXISTS (
  SELECT 1 FROM ticket_order_seats x
  WHERE x.order_id = o.id AND x.sector_type = s.sector AND x.row_num = s.row_num AND x.seat_num = s.seat_num
);

-- ──────────────── 6.1. Афиши для БД, где скрипт уже запускался ────────────────
-- Идемпотентно проставляем image_path там, где он ещё не задан
-- (вставки концертов выше защищены WHERE NOT EXISTS и сами себя не обновят).
UPDATE concert_events SET image_path = 'afisha_russkie_narodnye.jpg'      WHERE artist_group_name = 'Оркестр русских народных инструментов' AND image_path IS NULL;
UPDATE concert_events SET image_path = 'afisha_klassicheskaya_simfoniya.jpg' WHERE artist_group_name = 'Классическая симфония'            AND image_path IS NULL;
UPDATE concert_events SET image_path = 'afisha_etno_kuzbass.jpg'          WHERE artist_group_name = 'Этно Кузбасс — фольклорный ансамбль'  AND image_path IS NULL;
UPDATE concert_events SET image_path = 'afisha_taisia_polyakova.jpg'      WHERE artist_group_name = 'Таисия Полякова — камерный вечер'      AND image_path IS NULL;
UPDATE concert_events SET image_path = 'afisha_kosmos_shou.jpg'           WHERE artist_group_name = 'Космос: планетарий-шоу с оркестром'    AND image_path IS NULL;
UPDATE concert_events SET image_path = 'afisha_lebedinoe_ozero.jpg'       WHERE artist_group_name = 'Весенний балет «Лебединое озеро»'      AND image_path IS NULL;

-- ───────────────────── 7. Очистка демо-записей admin (если была выполнена ранняя версия скрипта) ─────────────────────
-- Роль «Администратор» и учётка admin не входят в ТЗ. Если вы уже запускали
-- предыдущую версию скрипта и они попали в вашу БД, удалите их вручную:
--
--   DELETE FROM users WHERE login = 'admin';
--   DELETE FROM roles WHERE name = 'Администратор' AND NOT EXISTS (SELECT 1 FROM users u WHERE u.role_id = roles.id);
--
-- (user_id у созданных admin-ом заказов при DELETE FROM users обнулится — ON DELETE SET NULL.)

-- ───────────────────── 8. Проверка результата ─────────────────────
SELECT 'roles'              AS table_name, COUNT(*) AS rows_count FROM roles
UNION ALL SELECT 'users', COUNT(*) FROM users
UNION ALL SELECT 'concert_halls', COUNT(*) FROM concert_halls
UNION ALL SELECT 'concert_events', COUNT(*) FROM concert_events
UNION ALL SELECT 'ticket_buyers', COUNT(*) FROM ticket_buyers
UNION ALL SELECT 'ticket_orders', COUNT(*) FROM ticket_orders
UNION ALL SELECT 'ticket_order_seats', COUNT(*) FROM ticket_order_seats;

-- Пример выборки заказов (шапка + позиции)
SELECT o.order_reference, b.full_name AS buyer, e.artist_group_name, o.status, o.total_amount
FROM ticket_orders o
JOIN ticket_buyers b ON b.id = o.buyer_id
JOIN concert_events e ON e.id = o.event_id
ORDER BY o.order_datetime;
