# Scheduling Domain — Regras de Implementação v1

> Status: Draft v1
> Data: 2026-09-11

## Objetivo

Detalhar o comportamento implementável do domínio de Scheduling da Virtual Employee Platform antes da criação do ERD físico, entidades EF Core e migrations PostgreSQL.

O Scheduling Engine é o coração operacional do produto. Ele deve garantir disponibilidade correta, impedir double-booking, respeitar timezone do negócio e preservar consistência mesmo sob concorrência.

---

## 1. Responsabilidades do Scheduling

O módulo de Scheduling é responsável por:

- calcular slots disponíveis;
- validar profissional e serviço;
- criar appointments;
- reagendar appointments;
- cancelar appointments;
- bloquear períodos;
- localizar appointments afetados por bloqueios;
- preservar histórico operacional;
- impedir conflitos de agenda;
- emitir eventos de domínio relacionados ao ciclo do appointment.

O Scheduling não é responsável por:

- interpretar linguagem natural;
- enviar mensagens WhatsApp;
- processar cartão/Pix;
- confirmar pagamento por conta própria;
- calcular analytics;
- executar refund diretamente.

---

## 2. Timezone e armazenamento temporal

### Decisão

- Todo instante absoluto será armazenado em PostgreSQL usando `timestamptz`.
- A aplicação trabalhará internamente em UTC para persistência e comparação.
- Cada Business terá `Timezone` em formato IANA, por exemplo `America/Sao_Paulo`.
- Regras recorrentes de disponibilidade (`AvailabilityRule`) serão armazenadas como horário local do estabelecimento.

### Exemplo

Business:

```text
Timezone = America/Sao_Paulo
```

AvailabilityRule:

```text
Monday 09:00 -> 18:00
```

Ao calcular slots para uma data específica, a aplicação converte a janela local para instantes UTC respeitando regras de timezone.

### Regra

Nunca persistir `DateTime` sem semântica clara de timezone no domínio.

Na implementação .NET, preferir tipos que preservem intenção temporal e conversões explícitas.

---

## 3. Disponibilidade

A disponibilidade efetiva é calculada a partir de:

```text
AvailabilityRule
- ScheduleBlock
- Appointments ativos
= Slots disponíveis
```

Não haverá uma tabela de slots materializados no MVP.

### AvailabilityRule

Representa uma janela recorrente por dia da semana.

Exemplo:

```text
ProfessionalId: P1
Monday: 09:00 -> 12:00
Monday: 13:00 -> 18:00
```

Pode existir mais de uma janela por dia.

### ScheduleBlock

Representa indisponibilidade excepcional.

Exemplos:

```text
2026-09-18 14:00 -> 18:00
2026-09-21 00:00 -> 2026-09-25 23:59
```

Pode ser:

- específico de um profissional;
- do estabelecimento inteiro, quando `ProfessionalId` for nulo.

---

## 4. Geração de slots

Entrada conceitual:

```text
TenantId
ServiceId
Date
ProfessionalId? (opcional)
```

Fluxo:

1. carregar Service ativo;
2. localizar profissionais habilitados para o Service;
3. se `ProfessionalId` informado, validar vínculo ProfessionalService;
4. carregar AvailabilityRules da data/dia da semana;
5. carregar ScheduleBlocks sobrepostos;
6. carregar Appointments ativos sobrepostos;
7. gerar candidatos conforme `Service.DurationMinutes`;
8. remover candidatos conflitantes;
9. retornar somente horários válidos.

### Granularidade de início

Para o MVP, adotar intervalo configurável de início, com default de **15 minutos**.

Exemplo para serviço de 45 minutos:

```text
09:00-09:45
09:15-10:00
09:30-10:15
...
```

Posteriormente o estabelecimento poderá configurar granularidade diferente.

### Observação

Slot retornado pela consulta é apenas uma oportunidade de reserva naquele instante; não representa lock e não garante disponibilidade até a gravação do Appointment.

---

## 5. Estados de Appointment relevantes para conflito

Appointments que ocupam agenda no MVP:

- `PENDING`
- `CONFIRMED`
- `CONFIRMED_BY_CLIENT`
- `RESCHEDULE_REQUESTED` enquanto ainda mantém o horário atual

Appointments que **não** ocupam agenda:

- `CANCELLED_BY_CLIENT`
- `CANCELLED_BY_BUSINESS`
- `COMPLETED`
- `NO_SHOW`

### RESCHEDULED

`RESCHEDULED` não deve ser usado como um estado terminal permanente para o registro atual do appointment quando isso gerar ambiguidade operacional.

Decisão recomendada para implementação:

- o mesmo Appointment mantém o novo horário;
- a mudança é registrada em `AppointmentHistory`;
- o estado operacional retorna para `CONFIRMED` ou `PENDING`, conforme a situação de pagamento;
- o evento `AppointmentRescheduled` registra a transição.

Assim evitamos que um appointment futuro fique preso em um estado sem semântica clara de ocupação.

---

## 6. Criação de Appointment

Command conceitual:

```text
CreateAppointment(
    TenantId,
    CustomerId,
    ProfessionalId,
    ServiceId,
    StartsAt,
    CorrelationId,
    IdempotencyKey?
)
```

Validações:

1. Tenant ativo;
2. Business ativo;
3. Service ativo;
4. Professional ativo;
5. Professional habilitado para Service;
6. StartsAt dentro de AvailabilityRule;
7. intervalo não cruza ScheduleBlock;
8. intervalo não cruza Appointment ativo;
9. preço e duração obtidos do Service atual;
10. gerar snapshots no Appointment;
11. persistir de forma transacional;
12. constraint de banco realiza última barreira contra concorrência.

Resultado:

```text
AppointmentId
Status = PENDING (quando exige pagamento)
ou
Status = CONFIRMED (quando política não exige pagamento)
```

Evento:

```text
AppointmentCreated
```

---

## 7. Double-booking e concorrência

### Problema

Dois clientes podem visualizar o mesmo slot disponível ao mesmo tempo.

Ambos podem tentar confirmar em milissegundos de diferença.

A checagem da aplicação sozinha é insuficiente.

### Estratégia

O sistema terá duas camadas:

#### Camada 1 — validação de aplicação

Antes de gravar:

```text
SELECT appointments sobrepostos ativos
```

Se encontrar conflito, retorna `SlotUnavailable`.

#### Camada 2 — proteção no PostgreSQL

A persistência deve impedir sobreposição mesmo quando duas transações passam na validação simultaneamente.

Estratégia preferida: `EXCLUDE CONSTRAINT` usando range temporal PostgreSQL.

Exemplo conceitual:

```sql
EXCLUDE USING gist (
    tenant_id WITH =,
    professional_id WITH =,
    tstzrange(starts_at, ends_at, '[)') WITH &&
)
WHERE (status IN ('PENDING', 'CONFIRMED', 'CONFIRMED_BY_CLIENT', 'RESCHEDULE_REQUESTED'));
```

Observação: a sintaxe final depende do modelo físico e extensões necessárias, como `btree_gist`.

### Intervalo `[)`

Usar intervalo semiaberto:

```text
[StartsAt, EndsAt)
```

Assim:

```text
09:00-10:00
10:00-11:00
```

não são considerados conflitantes.

### Resultado esperado sob corrida

Cliente A e Cliente B visualizam o mesmo horário de 10:00 e tentam reservá-lo quase simultaneamente.

```text
Cliente A -> CreateAppointment
          -> validação OK
          -> commit OK
          -> horário passa a estar ocupado

Cliente B -> CreateAppointment alguns milissegundos depois
          -> sistema revalida o slot
          -> detecta que o horário já foi ocupado
          -> cancela/rejeita a tentativa de criação
          -> retorna SLOT_UNAVAILABLE
          -> informa ao cliente: "Desculpe, este horário acabou de ser preenchido."
          -> oferece novos horários disponíveis
```

Se as duas requisições passarem pela validação de aplicação antes de qualquer commit, a `EXCLUDE CONSTRAINT` do PostgreSQL decide o vencedor:

```text
A -> commit OK
B -> constraint violation
B -> aplicação converte a violação para SLOT_UNAVAILABLE
B -> nenhuma reserva duplicada é criada
B -> cliente recebe a mesma mensagem amigável e novos horários
```

A experiência do usuário deve ser a mesma independentemente de o conflito ter sido detectado na validação de aplicação ou pela constraint do PostgreSQL. O consumidor nunca deve receber erro SQL, HTTP 500 ou detalhes de concorrência.

Mensagem padrão do MVP:

> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

---

## 8. PENDING e reserva temporária

Quando o serviço exige pagamento, um Appointment `PENDING` ocupa o slot temporariamente.

Sem expiração, usuários poderiam abandonar checkout e bloquear agenda indefinidamente.

### Decisão v1

Adicionar:

```text
ReservationExpiresAt
```

Default sugerido:

```text
10 minutos
```

Configuração futura pode variar.

### Regra

Appointment PENDING ocupa agenda somente enquanto:

```text
ReservationExpiresAt > now()
```

Após expiração:

- worker/job marca como `CANCELLED_BY_SYSTEM` ou estado equivalente de expiração;
- slot volta a ficar disponível;
- pagamento tardio precisa ser tratado cuidadosamente.

### Ajuste necessário no enum

Adicionar estado:

```text
EXPIRED
```

em vez de reutilizar cancelamento de cliente/estabelecimento.

### Pagamento tardio

Se webhook de pagamento chegar após expiração:

1. não confirmar automaticamente um appointment cujo slot já possa ter sido ocupado;
2. validar estado do Appointment;
3. se expirado, abrir fluxo compensatório;
4. preferencialmente solicitar refund automático integral ou encaminhar política específica.

No MVP, regra mais segura:

> Payment confirmado para Appointment expirado não reativa a reserva. O sistema inicia compensação financeira e informa o cliente.

---

## 9. Reagendamento

Command conceitual:

```text
RescheduleAppointment(
    TenantId,
    AppointmentId,
    NewProfessionalId,
    NewStartsAt,
    RequestedBy,
    CorrelationId
)
```

Fluxo:

1. carregar Appointment;
2. validar estado atual;
3. calcular novo `EndsAt` a partir do snapshot/duração aplicável;
4. validar ProfessionalService;
5. validar AvailabilityRule;
6. validar ScheduleBlock;
7. validar conflito com outros Appointments;
8. persistir novo horário na mesma transação;
9. gravar AppointmentHistory com horário anterior e novo;
10. emitir `AppointmentRescheduled`.

### Pagamento

Se o mesmo serviço/preço continua:

- Payment existente permanece associado;
- não criar nova cobrança.

Mudança para outro serviço/preço fica fora do fluxo simples de reagendamento do MVP; deve ser tratada como cancelamento + novo booking ou fluxo futuro específico.

---

## 10. Cancelamento

Command conceitual:

```text
CancelAppointment(
    TenantId,
    AppointmentId,
    CancelledBy,
    Reason,
    CorrelationId
)
```

Fluxo:

1. validar Appointment existente;
2. validar estado cancelável;
3. transicionar para `CANCELLED_BY_CLIENT` ou `CANCELLED_BY_BUSINESS`;
4. gravar histórico;
5. liberar slot imediatamente;
6. emitir `AppointmentCancelled`;
7. Refund Policy avalia elegibilidade de forma separada.

O cancelamento do Appointment não deve esperar o gateway concluir refund.

---

## 11. Bloqueio de agenda e indisponibilidade do negócio

Command conceitual:

```text
CreateScheduleBlock(
    TenantId,
    ProfessionalId?,
    StartsAt,
    EndsAt,
    Reason
)
```

Antes de confirmar uma indisponibilidade relevante, o sistema deve consultar:

```text
GetAffectedAppointments
```

Resposta conceitual:

```text
AffectedCount
Appointments[]
```

Fluxo ideal via WhatsApp Admin:

```text
"Não vou trabalhar amanhã à tarde"
  -> IA interpreta período/profissional
  -> Scheduling.GetAffectedAppointments
  -> sistema mostra impacto
  -> proprietário confirma
  -> ScheduleBlock criado
  -> evento ScheduleBlocked
  -> clientes afetados recebem opções
```

### Regra

Nenhum Appointment afetado é movido automaticamente.

Cliente deve escolher:

- novo horário;
- cancelamento.

---

## 12. Idempotência

Operações mutáveis expostas por API/integration layer devem aceitar idempotência quando houver risco de retry.

Prioridade:

- `CreateAppointment`
- `RescheduleAppointment`
- `CancelAppointment`
- `CreateScheduleBlock`

Formato lógico sugerido:

```text
TenantId + OperationType + IdempotencyKey
```

Uma repetição com a mesma chave e mesmo payload retorna o resultado anterior.

Mesmo key com payload incompatível deve gerar conflito.

---

## 13. Eventos do Scheduling

Eventos principais:

```text
AppointmentCreated
AppointmentConfirmed
AppointmentRescheduled
AppointmentCancelled
AppointmentExpired
AppointmentCompleted
NoShowRegistered
ScheduleBlocked
```

Envelope padrão:

```text
EventId
EventType
EventVersion
OccurredAt
TenantId
CorrelationId
CausationId
AggregateId
Payload
```

---

## 14. Interfaces de aplicação iniciais

### Queries

```text
GetAvailableSlots
GetAppointment
GetAppointmentsByPeriod
GetAffectedAppointments
GetProfessionalSchedule
```

### Commands

```text
CreateAppointment
RescheduleAppointment
CancelAppointment
CreateScheduleBlock
RemoveScheduleBlock
ConfirmAppointment
RegisterNoShow
CompleteAppointment
ExpirePendingAppointment
```

Esses contratos são de aplicação e não devem expor EF Core nem entidades persistidas diretamente.

---

## 15. Tratamento de falhas

### SlotUnavailable

Quando ocorre conflito:

```text
409 Conflict
code: SLOT_UNAVAILABLE
```

Resposta amigável para o canal:

> **Desculpe, este horário acabou de ser preenchido. Escolha um dos horários disponíveis abaixo.**

A aplicação deve atualizar/recalcular os horários e, sempre que possível, devolver alternativas válidas no mesmo fluxo para evitar que o cliente tenha de reiniciar a conversa.

### InvalidBusinessRule

Exemplos:

- Professional não executa Service;
- horário fora da disponibilidade;
- appointment não pode ser reagendado naquele estado.

Retorno de domínio não deve carregar detalhes internos de infraestrutura.

### Concurrency conflict

Constraint violation conhecida deve ser traduzida para erro de negócio previsível, nunca HTTP 500 genérico quando representar disputa válida por slot.

---

## 16. Índices previstos para o ERD físico

Ainda serão detalhados no modelo físico, mas o Scheduling exigirá pelo menos:

```text
Appointments(TenantId, ProfessionalId, StartsAt)
Appointments(TenantId, CustomerId, StartsAt)
Appointments(TenantId, Status, StartsAt)
AvailabilityRules(TenantId, ProfessionalId, DayOfWeek)
ScheduleBlocks(TenantId, ProfessionalId, StartsAt, EndsAt)
ProfessionalServices(TenantId, ProfessionalId, ServiceId)
```

Além da constraint temporal de exclusão para conflitos ativos.

---

## 17. Fluxo crítico end-to-end

```text
Cliente
  -> WhatsApp
  -> Conversation
  -> AI Gateway interpreta intenção
  -> Scheduling.GetAvailableSlots
  -> cliente escolhe
  -> Scheduling.CreateAppointment
  -> Appointment PENDING + ReservationExpiresAt
  -> Payments.CreatePaymentOrder
  -> hosted checkout
  -> gateway
  -> webhook validado
  -> PaymentConfirmed
  -> Scheduling.ConfirmAppointment
  -> Appointment CONFIRMED
  -> AppointmentConfirmed
  -> Messaging envia confirmação
  -> Reminder Scheduler agenda lembrete
```

A IA nunca confirma disponibilidade ou pagamento sem resultado estruturado do backend.

---

## 18. Decisões fechadas nesta versão

1. PostgreSQL `timestamptz` para instantes absolutos.
2. `Business.Timezone` IANA obrigatório.
3. AvailabilityRule é recorrente/local; não materializar Slot no MVP.
4. ScheduleBlock representa exceções.
5. Intervalos de appointment usam semântica `[start, end)`.
6. Double-booking protegido pela aplicação **e** pelo PostgreSQL.
7. Preferência por exclusion constraint com range temporal.
8. PENDING ocupa slot temporariamente.
9. PENDING terá `ReservationExpiresAt`.
10. Default inicial de hold de checkout: 10 minutos.
11. Adicionar estado `EXPIRED` ao Appointment.
12. Pagamento tardio após expiração não reativa automaticamente a reserva.
13. Reagendamento mantém o mesmo Appointment e registra histórico.
14. Mudança de serviço/preço não entra no reagendamento simples do MVP.
15. Cancelamento libera agenda independentemente do tempo do refund.
16. Nenhuma realocação automática sem consentimento do cliente.
17. Em disputa por slot, somente a primeira reserva válida é persistida; as demais recebem `SLOT_UNAVAILABLE`, mensagem amigável e novos horários.

---

## 19. Próximo passo

Com estas regras fechadas, o próximo artefato é o **ERD físico v1**.

Ele deve traduzir o domínio em tabelas, PKs, FKs, constraints, índices e tipos PostgreSQL para:

- tenants
- businesses
- users
- services
- professionals
- professional_services
- availability_rules
- schedule_blocks
- customers
- appointments
- appointment_history
- payments
- refunds
- conversations
- subscriptions
- usage_records
- webhook_inbox
- outbox_messages

Após o ERD físico, podemos criar os contratos de API e a estrutura inicial da solution .NET.
