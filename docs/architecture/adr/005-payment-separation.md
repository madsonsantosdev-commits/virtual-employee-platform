# ADR-005 — Separação entre Customer Payments e SaaS Billing

- **Status:** Accepted
- **Data:** 2026-09-10

## Contexto

A plataforma possui dois fluxos financeiros de naturezas diferentes:

1. o cliente final paga ao estabelecimento pelo serviço agendado;
2. o estabelecimento paga à plataforma pela assinatura SaaS.

Misturar esses fluxos aumentaria complexidade financeira, contábil, jurídica e operacional, além de aproximar a plataforma desnecessariamente de um modelo de custódia ou intermediação dos valores dos serviços.

## Decisão

**Customer Payments e SaaS Billing serão módulos e fluxos financeiros independentes.**

### Customer Payments

```text
Cliente final
    ↓
Gateway de pagamento
    ↓
Conta/saldo do estabelecimento no provedor
```

A plataforma:

- cria/orquestra a PaymentOrder;
- gera checkout seguro através do provedor;
- recebe webhook;
- acompanha o estado;
- associa pagamento ao appointment;
- aplica regras de refund através do gateway.

A plataforma não deve custodiar os valores pagos pelo cliente pelo serviço.

### SaaS Billing

```text
Estabelecimento
    ↓
SaaS Billing Provider
    ↓
Conta PJ da plataforma
```

A assinatura SaaS é uma relação comercial independente.

Modelo inicial:

- mensal: cartão recorrente ou Pix;
- anual com compromisso de 12 meses: cartão recorrente;
- sem boleto.

## Regras de pagamento de serviço

No MVP:

- pagamento integral;
- valor exato registrado em `Service.Price`;
- Pix, crédito e débito quando suportados;
- sem sinal;
- sem pagamento parcial;
- sem valor arbitrário;
- sem taxa própria da plataforma por appointment;
- refund integral quando aplicável;
- sem refund parcial.

## Regras arquiteturais

- Billing não depende de Payments;
- Payments não depende de Billing;
- assinatura SaaS não é descontada das vendas do estabelecimento;
- dados de cartão nunca trafegam pelo Conversation Engine ou AI Gateway;
- checkout deve ser hospedado/tokenizado pelo provedor;
- redirect do navegador não confirma pagamento;
- confirmação ocorre somente após webhook/API confiável do gateway;
- webhooks financeiros devem ser autenticados, idempotentes e auditáveis.

## Refund

Refund é relacionado a Customer Payments, não a SaaS Billing.

Fluxo conceitual:

```text
Cancelamento / regra autorizada
      ↓
Refund Request
      ↓
Gateway
      ↓
Webhook / confirmação
      ↓
RefundCompleted
```

Nunca comunicar ao cliente que o valor foi estornado antes da confirmação do gateway.

## Consequências positivas

- separação contábil e arquitetural clara;
- menor risco de custódia indevida;
- possibilidade de trocar o provedor de Billing sem afetar Customer Payments;
- possibilidade de trocar o gateway do estabelecimento sem afetar assinatura SaaS;
- regras financeiras mais simples de auditar e testar.

## Consequências negativas

- duas integrações financeiras podem ser necessárias;
- dois conjuntos de webhooks e estados precisam ser monitorados;
- operação financeira exige observabilidade e idempotência específicas para cada fluxo.
