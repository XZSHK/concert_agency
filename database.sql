-- ============================================================================
-- Концертно-гастрольное и билетное агентство «Арт-Престиж» (Вариант 30, КТЗ)
-- База данных MySQL: Concert_Agency, 7 таблиц по ТЗ.
--
-- Если БД уже существует на вашем компьютере — этот файл НЕ сломает её:
-- таблицы создаются только если их нет (IF NOT EXISTS), тестовые данные
-- вставляются через INSERT IGNORE (повторный запуск безопасен).
--
-- Расширения сверх ТЗ (нужны существующему приложению, помечены EXT):
--   EXT ticket_buyers.password   — вход покупателей по email (в ТЗ у покупателей
--                                  пароля нет, вход сотрудников — через users).
--   EXT concert_events.image_path — файл афиши в папке img/.
-- ============================================================================

CREATE DATABASE IF NOT EXISTS Concert_Agency
  DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

USE Concert_Agency;

-- 1. roles (id, name) -------------------------------------------------------
CREATE TABLE IF NOT EXISTS roles (
    id   INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 2. users (id, role_id, login, password_hash, full_name) -------------------
CREATE TABLE IF NOT EXISTS users (
    id            INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    role_id       INT UNSIGNED NOT NULL,
    login         VARCHAR(50)  NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL COMMENT 'SHA256 hex (PasswordHasher)',
    full_name     VARCHAR(150) NOT NULL,
    CONSTRAINT fk_users_role FOREIGN KEY (role_id) REFERENCES roles(id)
        ON UPDATE CASCADE ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 3. concert_halls (id, hall_name, city, parterre_capacity, vip_capacity,
--                   base_rent_per_day) --------------------------------------
CREATE TABLE IF NOT EXISTS concert_halls (
    id                  INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    hall_name           VARCHAR(150) NOT NULL,
    city                VARCHAR(100) NOT NULL,
    parterre_capacity   INT NOT NULL DEFAULT 0,
    vip_capacity        INT NOT NULL DEFAULT 0,
    base_rent_per_day   DECIMAL(12,2) NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 4. concert_events (id, hall_id, artist_group_name, event_date,
--                    age_restriction, parterre_price, vip_price) ------------
CREATE TABLE IF NOT EXISTS concert_events (
    id                 INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    hall_id            INT UNSIGNED NOT NULL,
    artist_group_name  VARCHAR(150) NOT NULL,
    event_date         DATETIME NOT NULL,
    age_restriction    VARCHAR(10) NOT NULL DEFAULT '0+',
    parterre_price     DECIMAL(12,2) NOT NULL DEFAULT 0,
    vip_price          DECIMAL(12,2) NOT NULL DEFAULT 0,
    image_path         VARCHAR(255) NULL COMMENT 'EXT: файл афиши в img/',
    CONSTRAINT fk_events_hall FOREIGN KEY (hall_id) REFERENCES concert_halls(id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    INDEX idx_events_date (event_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 5. ticket_buyers (id, full_name, email, phone, has_promo_subscription) -----
CREATE TABLE IF NOT EXISTS ticket_buyers (
    id                      INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    full_name               VARCHAR(150) NOT NULL,
    email                   VARCHAR(150) NOT NULL UNIQUE,
    phone                   VARCHAR(30) NULL,
    has_promo_subscription  TINYINT(1) NOT NULL DEFAULT 0,
    password                VARCHAR(255) NULL COMMENT 'EXT: SHA256 hex, вход покупателей'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 6. ticket_orders (id, order_reference, user_id, buyer_id, event_id,
--                   order_datetime, service_fee, total_amount, status,
--                   cancel_reason) ------------------------------------------
CREATE TABLE IF NOT EXISTS ticket_orders (
    id               INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    order_reference  VARCHAR(30) NOT NULL UNIQUE COMMENT 'Маска ТЗ: PR23-KTZ-{Год}-{Номер:D5}',
    user_id          INT UNSIGNED NULL COMMENT 'Сотрудник, оформивший заказ (если есть)',
    buyer_id         INT UNSIGNED NOT NULL,
    event_id         INT UNSIGNED NOT NULL,
    order_datetime   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    service_fee      DECIMAL(12,2) NOT NULL DEFAULT 0,
    total_amount     DECIMAL(12,2) NOT NULL DEFAULT 0,
    status           VARCHAR(40) NOT NULL DEFAULT 'Бронь (15 мин)'
                     COMMENT 'FSM: Бронь (15 мин) -> Оплачен -> Выписан -> Контроль на входе пройден / Возврат билета',
    cancel_reason    VARCHAR(255) NULL,
    CONSTRAINT fk_orders_buyer FOREIGN KEY (buyer_id) REFERENCES ticket_buyers(id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    CONSTRAINT fk_orders_event FOREIGN KEY (event_id) REFERENCES concert_events(id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    INDEX idx_orders_buyer (buyer_id),
    INDEX idx_orders_event (event_id),
    INDEX idx_orders_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 7. ticket_order_seats (id, order_id, sector_type, row_num, seat_num,
--                        base_seat_price, final_seat_cost) ------------------
CREATE TABLE IF NOT EXISTS ticket_order_seats (
    id               INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    order_id         INT UNSIGNED NOT NULL,
    sector_type      VARCHAR(20) NOT NULL COMMENT 'Партер | VIP',
    row_num          INT NOT NULL DEFAULT 1,
    seat_num         INT NOT NULL,
    base_seat_price  DECIMAL(12,2) NOT NULL DEFAULT 0,
    final_seat_cost  DECIMAL(12,2) NOT NULL DEFAULT 0 COMMENT 'VIP: base + 500 (бейдж)',
    CONSTRAINT fk_seats_order FOREIGN KEY (order_id) REFERENCES ticket_orders(id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    INDEX idx_seats_order (order_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============================================================================
-- Тестовые данные (INSERT IGNORE — безопасно при повторном запуске)
-- Пароли (SHA256 hex, см. Core/PasswordHasher):
--   producer123 -> f4156105c167be3a85ad995b661a701a54aaec63c12af53a146d57183aad4fa2
--   manager123  -> 866485796cfa8d7c0cf7111640205b83076433547577511d81f8030ae99ecea5
--   123456      -> 8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92
-- ============================================================================

INSERT IGNORE INTO roles (id, name) VALUES
    (1, 'Продюсер гастрольных туров'),
    (2, 'Менеджер билетной программы');

INSERT IGNORE INTO users (id, role_id, login, password_hash, full_name) VALUES
    (1, 1, 'producer', 'f4156105c167be3a85ad995b661a701a54aaec63c12af53a146d57183aad4fa2', 'Петров Пётр Петрович'),
    (2, 2, 'manager',  '866485796cfa8d7c0cf7111640205b83076433547577511d81f8030ae99ecea5', 'Сидорова Анна Сергеевна');

INSERT IGNORE INTO concert_halls (id, hall_name, city, parterre_capacity, vip_capacity, base_rent_per_day) VALUES
    (1, 'Филармония им. Шостаковича', 'Кемерово', 800, 100, 150000.00),
    (2, 'Театр драмы', 'Новосибирск', 500, 60, 120000.00),
    (3, 'ДК «Победа»', 'Кемерово', 350, 40, 80000.00);

INSERT IGNORE INTO concert_events (id, hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price) VALUES
    (1, 1, 'ВИА «Самоцветы»', '2026-12-10 19:00:00', '6+',  1500.00, 3000.00),
    (2, 2, 'Хор «Сибирский»',  '2026-12-15 19:00:00', '0+',  1200.00, 2500.00),
    (3, 3, 'Группа «Север»',   '2026-12-20 20:00:00', '12+', 1000.00, 2200.00);

INSERT IGNORE INTO ticket_buyers (id, full_name, email, phone, has_promo_subscription, password) VALUES
    (1, 'Иванов Иван Иванович', 'ivan@example.ru', '+7 (999) 123-45-67', 1, '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92'),
    (2, 'Петрова Мария Олеговна', 'maria@example.ru', '+7 (999) 987-65-43', 0, '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92');
