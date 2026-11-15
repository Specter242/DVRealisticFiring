using System.Globalization;
using DV.CashRegister;
using DV.ServicePenalty.UI;
using TMPro;

namespace RealisticFiring.Upgrades
{
 internal sealed class UpgradePayingScreen : DisplayScreen
    {
        private DisplayScreenSwitcher kiosk;
        private UpgradesScreen list;
        private CareerManagerInfoScreen info;
        private CashRegisterCareerManager register;
        private TextMeshPro title1, title2, upgradeName, price, insertWallet, depositedText, deposited;

        private UpgradesScreen.Upgrade upgrade;

        /// <summary>Whether the kiosk has the stock payment screen this borrows from.</summary>
        internal bool Ready => register != null && deposited != null;

        internal void Init(DisplayScreenSwitcher kiosk, UpgradesScreen list, CareerManagerInfoScreen info)
        {
            this.kiosk = kiosk;
            this.list = list;
            this.info = info;
            var stock = kiosk.GetComponentInChildren<CareerManagerLicensePayingScreen>(true);
            if (stock == null)
                return;
            register = stock.cashReg;
            title1 = stock.title1;
            title2 = stock.title2;
            upgradeName = stock.licenseNameText;
            price = stock.licensePriceText;
            insertWallet = stock.insertWallet;
            depositedText = stock.depositedText;
            deposited = stock.depositedValue;
        }

        /// <summary>The upgrade the screen sells next.</summary>
        internal void Sell(UpgradesScreen.Upgrade upgrade) => this.upgrade = upgrade;

        public override void Activate(IDisplayScreen previousScreen)
        {
            register.SetTotalCost(upgrade.Price);
            register.CashAdded += ShowDeposited;
            register.CashRemoved += ShowDeposited;
            title1.text = UpgradesMenu.Entry;
            title2.text = "Upgrade:";
            upgradeName.text = upgrade.Name;
            price.text = Dollars(upgrade.Price);
            insertWallet.text = CareerManagerLocalization.INSERT_WALLET_TO_PAY;
            depositedText.text = CareerManagerLocalization.DEPOSITED;
            ShowDeposited();
        }

        public override void Disable()
        {
            register.ClearCurrentTransaction();
            register.CashAdded -= ShowDeposited;
            register.CashRemoved -= ShowDeposited;
            title1.text = title2.text = upgradeName.text = price.text = insertWallet.text = depositedText.text = deposited.text = string.Empty;
        }

        public override void HandleInputAction(InputAction input)
        {
            switch (input)
            {
                case InputAction.Cancel:
                    kiosk.SetActiveDisplay(list);
                    break;
                case InputAction.Confirm:
                    // Short, the register plays its not-enough sound and the screen stays.
                    if (upgrade == null || !upgrade.CanBuy || !register.Buy())
                        break;
                    upgrade.Buy();
                    Main.Mod?.Logger.Log($"Bought the {upgrade.Name} upgrade for {Dollars(upgrade.Price)}: {upgrade.Engines} has it now");
                    UpgradesMenu.ShowInfo(info, list, upgrade.Name, upgrade.BoughtLine);
                    kiosk.SetActiveDisplay(info);
                    break;
            }
        }

        private void ShowDeposited() => deposited.text = Dollars(register.DepositedCash);

        private static string Dollars(double amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
