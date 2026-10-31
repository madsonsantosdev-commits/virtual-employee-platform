# Validação manual Mercado Pago — 31/10/2026

Ambiente: teste (`live_mode=false`), API local, PostgreSQL e túnel HTTPS.
Baseline: `315775c` (merge do PR #31).

## Evidências observadas

- Worker de reservas habilitado explicitamente: reserva anterior passou de
  PENDING para EXPIRED; criação posterior no mesmo horário foi aceita.
- Checkout de cartão aprovado: R$ 65,00, BRL.
- Ordem paga: `ORDTST01M69TC187M49WQV23DJ6MNM5M`.
- Appointment: `2f1ca175-f58d-49a7-b2f5-41efd1cf6145`.
- Tentativa confirmadora: `3bd99a6a-424c-43bd-bca7-4fa03d8ed571`.
- Payment: `d566e66a-8b94-4ff6-8a1c-6c4c0bc68fa8`, CONFIRMED, R$ 65,00.
- Confirmação persistida em `2026-10-31T20:38:03.848574Z`, antes do prazo
  `2026-10-31T20:38:13.015285Z` da reserva.
- Reenvio via simulador preservou CONFIRMED e updatedAt do Appointment.
- Histórico consultado após reenvio: exatamente CREATED e PAYMENT_CONFIRMED.
- Consulta financeira: um Payment, duas tentativas, somente a original confirmada.

O túnel inicialmente encaminhava para 5221, enquanto a API atendia em 5222.
A primeira entrega retornou 502. O destino do túnel e a URL do webhook foram
corrigidos. A evidência não determina qual entrega/retry confirmou o pagamento;
a data persistida estabelece que o processamento ocorreu antes do vencimento.

## Pendência reproduzida

Uma execução adicional do script criou a tentativa
`5c82a279-cff9-45e4-a2f3-48cacc767283` no mesmo Payment, após falha na criação
de outro Appointment. O PowerShell conservou a variável antiga.
A ordem `ORDTST01M69TS4JES53ARW1827GZTJFB` venceu sem pagamento e notificou
`canceled/expired`, mas a tentativa local permaneceu PENDING; a entrega retornou 503.

## Tratamento incremental

O webhook mantém a confirmação aprovada como primeiro caminho. Se ela não puder
ser verificada, uma segunda consulta autenticada da ordem pode reconhecer
`online/canceled/expired`, conferindo ordem, recebedor, aplicação, tentativa,
moeda e valor. Ordens com pagamentos ou valores pagos permanecem pendentes de
classificação/reconciliação. O corpo da notificação não é prova do estado.

Após o prazo local, a tentativa ativa passa para EXPIRED com a regra do domínio.
Um advisory lock no mesmo escopo da confirmação serializa as alterações.
Reenvio não reaplica a alteração. Tentativa confirmada é preservada.
Payment, Appointment e histórico operacional não são alterados por esse caminho.
Demais cancelamentos, rejeições, reconciliação automática e refunds continuam pendentes.

## Verificação local da alteração

Com TEST_DATABASE_CONNECTION_STRING apontando para o banco separado de testes:

```powershell
dotnet test tests/VirtualEmployee.IntegrationTests -c Release --filter "FullyQualifiedName~MercadoPagoWebhooksEndpointsTests|FullyQualifiedName~MercadoPagoExpiredOrderReaderTests"
```

Depois dos testes, reenviar uma notificação assinada da ordem vencida e consultar
a tentativa: EXPIRED; a tentativa original, Payment e Appointment devem continuar
CONFIRMED. Repetir o envio e conferir que updated_at da tentativa não muda.

## Resultado final da validação — 31/10/2026

- Suíte completa em Release: 1.763 testes passaram; zero falhas ou ignorados.
- Webhook da ordem vencida processado com sucesso.
- Segunda tentativa passou de PENDING para EXPIRED.
- Reenvio preservou EXPIRED e updated_at em 18:50:42.257 -03:00.
- Payment e tentativa original permaneceram CONFIRMED, com confirmação em 17:38:03.848 -03:00.
